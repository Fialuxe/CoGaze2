"""Fusing posed depth into a mesh.

A projective TSDF on the GPU, written directly against torch rather than using Open3D's
VoxelBlockGrid. Three reasons, in order of weight:

  * confidence weighting — integration weight should follow the estimator's per-pixel
    confidence, which Open3D's integrate() does not accept;
  * temporal decay — moving objects have to fade rather than accumulate, and Open3D has
    no decay mechanism;
  * the PyPI Open3D build carries no CUDA hash backend on Windows, so its grid would run
    on the CPU while a perfectly good GPU sits idle.

Two grids rather than one. Stereo depth error grows with the square of distance — about
1 mm at 0.5 m and 40 mm at 3 m for this rig — so a single voxel size either wastes
memory resolving noise far away or discards detail up close.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import torch

from depth import DepthResult


def incidence_weight(result: DepthResult, floor: float = 0.26) -> np.ndarray:
    """Down-weights surfaces seen edge-on.

    A depth error on a surface viewed at a grazing angle displaces the reconstructed
    point along that surface rather than across it, which is what draws the long streaks
    across a floor panned past quickly. Weighting by the cosine of the incidence angle
    suppresses those samples without discarding the surface, since the same floor seen
    face-on from another keyframe still contributes.

    The floor keeps a little weight at extreme angles rather than zero, so a surface only
    ever seen edge-on still appears rather than becoming a hole.
    """
    depth = np.where(np.isfinite(result.depth), result.depth, 0.0).astype(np.float32)
    camera = result.camera
    height, width = depth.shape

    cols, rows = np.meshgrid(np.arange(width, dtype=np.float32),
                             np.arange(height, dtype=np.float32))
    points = np.stack([(cols - camera.cx) * depth / camera.fx,
                       (rows - camera.cy) * depth / camera.fy,
                       depth], axis=-1)

    # Central differences give the two surface tangents; their cross product is the normal.
    du = np.zeros_like(points)
    dv = np.zeros_like(points)
    du[:, 1:-1] = points[:, 2:] - points[:, :-2]
    dv[1:-1, :] = points[2:, :] - points[:-2, :]

    normal = np.cross(du, dv)
    norm = np.linalg.norm(normal, axis=-1, keepdims=True)
    ray = points / np.maximum(np.linalg.norm(points, axis=-1, keepdims=True), 1e-6)
    cosine = np.abs(np.sum(normal / np.maximum(norm, 1e-12) * ray, axis=-1))

    # A depth discontinuity produces a huge cross product and a meaningless normal, so
    # treat those as unusable rather than trusting the angle.
    cosine[norm[..., 0] < 1e-9] = 0.0
    return np.clip(cosine, floor, 1.0).astype(np.float32)


@dataclass(frozen=True)
class ChunkMesh:
    """Geometry together with how well each part of it was measured."""
    mesh: "o3d.geometry.TriangleMesh"  # noqa: F821
    confidence: np.ndarray
    label: np.ndarray = None
    """Per vertex, a semantic label id, zero where nothing was recognised."""
    """Per vertex, in [0, 1], from accumulated integration weight. A surface glimpsed once
    edge-on and one seen twenty times face-on are both drawn; the expert should be able to
    tell them apart before pointing at either."""


@dataclass(frozen=True)
class GridSpec:
    name: str
    voxel_size: float
    depth_min: float
    depth_max: float
    weight_threshold: float
    """Truncation band, in voxels, either side of the surface."""
    truncation_voxels: float = 4.0
    """Ceiling on accumulated weight, so a long-observed voxel can still be overwritten
    when the scene changes rather than needing hundreds of frames to shift."""
    max_weight: float = 20.0
    """Accumulated weight at which a surface counts as fully measured, for reporting
    confidence to the expert."""
    confident_weight: float = 9.0


# 4 mm matches the noise floor at 0.4-1 m. Requiring three observations is free noise
# rejection here, because close surfaces are seen from many keyframes.
# The threshold stays at three observations even though incidence weighting shrinks each
# contribution: that is the point. A surface seen face-on reaches three quickly, while one
# only ever seen edge-on needs far more evidence before it is drawn. Raising the threshold
# on top of the weighting compounds the two and erases floors entirely.
NEAR = GridSpec("near", voxel_size=0.004, depth_min=0.3, depth_max=1.5,
                weight_threshold=3.0)

# 3 cm is already below the noise floor at 3 m. The threshold drops to one observation
# because a wall glimpsed once as the worker turns would otherwise never appear, and far
# geometry is exactly what most needs to be present.
FAR = GridSpec("far", voxel_size=0.03, depth_min=1.5, depth_max=7.0,
               weight_threshold=1.0)


class Grid:
    """A dense TSDF volume over a fixed box.

    Dense rather than hashed: the volumes here are bounded by the work area, so the
    simpler representation costs little and keeps the integration a single fused kernel.

    The volume is divided into fixed chunks. Integration records which chunks it touched,
    so meshing can be confined to those rather than re-extracting the whole volume — the
    difference between a step that scales with what changed and one that scales with how
    much has been mapped. Chunks are also the unit sent to the expert, so only the part of
    the scene that moved crosses the network.
    """

    CHUNK = 32
    """Voxels along a chunk edge: 12.8 cm in the near grid, 96 cm in the far one."""

    def __init__(self, spec: GridSpec, bounds_min, bounds_max, device: str = "cuda"):
        self.spec = spec
        self.dirty: set[tuple[int, int, int]] = set()
        self.device = torch.device(device)
        self.origin = torch.tensor(bounds_min, dtype=torch.float32, device=self.device)
        self.truncation = spec.truncation_voxels * spec.voxel_size

        extent = np.asarray(bounds_max) - np.asarray(bounds_min)
        self.dims = np.maximum(np.ceil(extent / spec.voxel_size).astype(int), 1)
        shape = tuple(int(d) for d in self.dims)

        self.tsdf = torch.ones(shape, dtype=torch.float32, device=self.device)
        self.weight = torch.zeros(shape, dtype=torch.float32, device=self.device)
        self.colour = torch.zeros(shape + (3,), dtype=torch.float32, device=self.device)
        # A label is a name rather than a quantity, so it cannot be averaged. Each voxel
        # keeps the label from its most confident observation and the weight that earned
        # it, both in a byte to keep a volume this size affordable.
        self.label = torch.zeros(shape, dtype=torch.uint8, device=self.device)
        self.label_weight = torch.zeros(shape, dtype=torch.uint8, device=self.device)

        # Voxel centres, held once; the grids are static so this is not recomputed.
        grids = torch.meshgrid(*[torch.arange(d, device=self.device, dtype=torch.float32)
                                 for d in shape], indexing="ij")
        self.centres = (torch.stack(grids, dim=-1) + 0.5) * spec.voxel_size + self.origin

    @property
    def voxel_count(self) -> int:
        return int(np.prod(self.dims))

    def decay(self, factor: float) -> None:
        """Ages every observation. Applied per keyframe, a factor of 0.8 halves a
        voxel's influence in about a second at 3 Hz, so a part that moves stops being
        supported by where it used to be."""
        self.weight *= factor

    @torch.no_grad()
    def integrate(self, result: DepthResult, colour: np.ndarray,
                  weights: np.ndarray | None = None,
                  labels: np.ndarray | None = None) -> None:
        """Adds one depth map. ``weights`` overrides the estimator's confidence, which is
        how incidence weighting is applied without recomputing it per grid."""
        camera = result.camera
        depth = torch.from_numpy(np.nan_to_num(result.depth, posinf=0.0, neginf=0.0)).to(self.device)
        confidence = torch.from_numpy(
            result.confidence if weights is None else weights).to(self.device)
        rgb = torch.from_numpy(np.ascontiguousarray(colour)).to(self.device).float() / 255.0
        label_map = (torch.from_numpy(np.ascontiguousarray(labels)).to(self.device)
                     if labels is not None else None)
        height, width = depth.shape

        world_to_camera = torch.from_numpy(np.linalg.inv(camera.pose)).to(self.device).float()
        points = self.centres.reshape(-1, 3)
        in_camera = points @ world_to_camera[:3, :3].T + world_to_camera[:3, 3]
        z = in_camera[:, 2]

        front = z > 1e-6
        u = torch.zeros_like(z)
        v = torch.zeros_like(z)
        u[front] = camera.fx * in_camera[front, 0] / z[front] + camera.cx
        v[front] = camera.fy * in_camera[front, 1] / z[front] + camera.cy

        cols = u.round().long()
        rows = v.round().long()
        inside = front & (cols >= 0) & (cols < width) & (rows >= 0) & (rows < height)
        if not bool(inside.any()):
            return

        index = rows[inside] * width + cols[inside]
        measured = depth.reshape(-1)[index]
        weight = confidence.reshape(-1)[index]
        sample = rgb.reshape(-1, 3)[index]
        sample_label = label_map.reshape(-1)[index] if label_map is not None else None

        # Distance from the voxel to the observed surface along the ray.
        sdf = measured - z[inside]
        usable = (measured >= self.spec.depth_min) & (measured <= self.spec.depth_max) \
            & (weight > 0) & (sdf >= -self.truncation)
        if not bool(usable.any()):
            return

        flat = torch.nonzero(inside, as_tuple=False).squeeze(1)[usable]
        sdf = (sdf[usable] / self.truncation).clamp(max=1.0)
        weight = weight[usable]
        sample = sample[usable]
        if sample_label is not None:
            sample_label = sample_label[usable]

        old_weight = self.weight.reshape(-1)[flat]
        new_weight = (old_weight + weight).clamp(max=self.spec.max_weight)
        blend = weight / new_weight.clamp(min=1e-8)

        tsdf = self.tsdf.reshape(-1)
        tsdf[flat] += blend * (sdf - tsdf[flat])
        colours = self.colour.reshape(-1, 3)
        colours[flat] += blend[:, None] * (sample - colours[flat])
        self.weight.reshape(-1)[flat] = new_weight

        if sample_label is not None:
            # Named voxels keep the label from whichever view saw them most strongly, so a
            # glancing misdetection cannot overwrite a confident one.
            scaled = (weight * 255.0 / self.spec.max_weight).clamp(0, 255).to(torch.uint8)
            stored = self.label_weight.reshape(-1)[flat]
            better = (sample_label > 0) & (scaled > stored)
            chosen = flat[better]
            self.label.reshape(-1)[chosen] = sample_label[better]
            self.label_weight.reshape(-1)[chosen] = scaled[better]

        self._mark_dirty(flat)

    def _mark_dirty(self, flat: torch.Tensor) -> None:
        """Records which chunks the touched voxels fall in."""
        dims = torch.tensor(self.dims.tolist(), device=flat.device)
        k = flat % dims[2]
        j = (flat // dims[2]) % dims[1]
        i = flat // (dims[1] * dims[2])
        chunks = torch.stack([i // self.CHUNK, j // self.CHUNK, k // self.CHUNK], dim=1)
        self.dirty.update(map(tuple, torch.unique(chunks, dim=0).cpu().numpy().tolist()))

    def chunk_mesh(self, key: tuple[int, int, int]):
        """Extracts one chunk.

        The slice extends one voxel past the chunk on each side, because marching cubes
        works between voxel centres and a chunk meshed in isolation would otherwise stop
        short of its own boundary, leaving cracks between neighbours.
        """
        import open3d as o3d
        from skimage import measure

        start = np.array(key) * self.CHUNK
        stop = np.minimum(start + self.CHUNK + 1, self.dims)
        if np.any(stop - start < 2):
            return None

        window = tuple(slice(int(a), int(b)) for a, b in zip(start, stop))
        weight = self.weight[window].cpu().numpy()
        volume = self.tsdf[window].cpu().numpy().copy()
        volume[weight < self.spec.weight_threshold] = 1.0
        if not ((volume < 0).any() and (volume > 0).any()):
            return None

        verts, faces, _, _ = measure.marching_cubes(volume, level=0.0)
        origin = self.origin.cpu().numpy() + start * self.spec.voxel_size
        positions = verts * self.spec.voxel_size + origin

        mesh = o3d.geometry.TriangleMesh(
            o3d.utility.Vector3dVector(positions.astype(np.float64)),
            o3d.utility.Vector3iVector(faces.astype(np.int32)))

        indices = np.clip(np.round(verts), 0, np.array(volume.shape) - 1).astype(int)
        rows = (indices[:, 0], indices[:, 1], indices[:, 2])
        mesh.vertex_colors = o3d.utility.Vector3dVector(
            self.colour[window].cpu().numpy()[rows].astype(np.float64))
        return ChunkMesh(mesh, self._confidence(weight[rows]),
                         self.label[window].cpu().numpy()[rows])

    def _confidence(self, weight: np.ndarray) -> np.ndarray:
        return np.clip(weight / self.spec.confident_weight, 0.0, 1.0).astype(np.float32)

    def take_dirty(self) -> set[tuple[int, int, int]]:
        """Returns the chunks changed since the last call and clears the record."""
        changed, self.dirty = self.dirty, set()
        return changed

    def mesh(self):
        """Extracts the zero crossing. Marching cubes runs on the CPU, which is
        acceptable because meshing happens far less often than integration."""
        import open3d as o3d
        from skimage import measure

        weight = self.weight.cpu().numpy()
        volume = self.tsdf.cpu().numpy().copy()
        # Unobserved and under-observed voxels are pushed outside the surface so they
        # cannot produce a crossing.
        volume[weight < self.spec.weight_threshold] = 1.0
        if not ((volume < 0).any() and (volume > 0).any()):
            return o3d.geometry.TriangleMesh()

        verts, faces, _, _ = measure.marching_cubes(volume, level=0.0)
        verts = verts * self.spec.voxel_size + self.origin.cpu().numpy()

        mesh = o3d.geometry.TriangleMesh(
            o3d.utility.Vector3dVector(verts.astype(np.float64)),
            o3d.utility.Vector3iVector(faces.astype(np.int32)))

        indices = np.clip(np.round((verts - self.origin.cpu().numpy()) / self.spec.voxel_size),
                          0, np.asarray(self.dims) - 1).astype(int)
        rows = (indices[:, 0], indices[:, 1], indices[:, 2])
        mesh.vertex_colors = o3d.utility.Vector3dVector(
            self.colour.cpu().numpy()[rows].astype(np.float64))
        return ChunkMesh(mesh, self._confidence(weight[rows]),
                         self.label.cpu().numpy()[rows])


def bounds_from(frames, results, percentile: float = 1.0, margin: float = 0.2):
    """Derives a volume from where the observed surfaces actually are, trimming
    percentile outliers so one bad depth value cannot inflate the box."""
    points = []
    for result in results:
        depth = result.depth
        valid = np.isfinite(depth) & (depth > 0)
        if not valid.any():
            continue
        rows, cols = np.nonzero(valid)
        step = max(1, len(rows) // 20000)
        rows, cols = rows[::step], cols[::step]
        z = depth[rows, cols]
        camera = result.camera
        local = np.stack([(cols - camera.cx) * z / camera.fx,
                          (rows - camera.cy) * z / camera.fy, z], axis=1)
        points.append(local @ camera.pose[:3, :3].T + camera.pose[:3, 3])

    if not points:
        raise ValueError("no valid depth to derive bounds from")
    cloud = np.concatenate(points)
    low = np.percentile(cloud, percentile, axis=0) - margin
    high = np.percentile(cloud, 100 - percentile, axis=0) + margin
    return low, high


def simplify(mesh, target: int):
    """Decimates towards a triangle budget, leaving smaller meshes alone."""
    if len(mesh.triangles) <= target:
        return mesh
    return mesh.simplify_quadric_decimation(target_number_of_triangles=target)
