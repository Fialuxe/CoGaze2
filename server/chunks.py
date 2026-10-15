"""Packaging mesh chunks for the expert.

Geometry is Draco-compressed. Measured on real chunks: 46.1 KB raw, 46.8 KB as plain
glTF, 18.4 KB gzipped, 6.2 KB with Draco. glTF is a container rather than a compression —
wrapping the same arrays in it costs slightly more than sending them bare, and the saving
attributed to it actually comes from Draco. So Draco is used directly and the container is
skipped.

That is not hand-rolled mesh handling. Unity's Draco package is the same native decoder
glTF loaders call for Draco-compressed meshes; using it directly reaches a Unity Mesh by
the same path, minus a layer that earns nothing here. What is written by hand is only the
envelope: which chunk, which epoch, how many bytes.

One message carries every chunk that changed. Confidence rides in the alpha channel of the
vertex colour, which Draco carries as a four-channel attribute.

Geometry stays in the server's right-handed, Y-down frame; the client applies the same Y
flip it uses on the way in, which is its own inverse.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import TYPE_CHECKING

import DracoPy
import numpy as np

if TYPE_CHECKING:  # annotations only, so the encoder runs without torch (make_synthetic_chunks.py)
    from fusion import ChunkMesh, Grid

MAGIC = b"CGZM"
VERSION = 1

LOD_CODES = {"near": 0, "far": 1}

# 14 bits over a 12.8 cm chunk resolves about 8 µm, far below the millimetre the sensor
# itself can resolve, so quantisation costs nothing measurable.
QUANTIZATION_BITS = 14
COMPRESSION_LEVEL = 7


@dataclass
class Update:
    """The chunks that changed, and the ones that no longer hold a surface."""
    epoch: int
    replaced: list[tuple[str, tuple[int, int, int], bytes, bytes]] = field(default_factory=list)
    removed: list[tuple[str, tuple[int, int, int]]] = field(default_factory=list)

    def encode(self) -> bytes:
        parts = [struct.pack("<4sHHII", MAGIC, VERSION, self.epoch,
                             len(self.replaced), len(self.removed))]
        for lod, key, _, _ in self.replaced:
            parts.append(struct.pack("<Biii", LOD_CODES[lod], *key))
        for lod, key in self.removed:
            parts.append(struct.pack("<Biii", LOD_CODES[lod], *key))
        for _, _, payload, labels in self.replaced:
            parts.append(struct.pack("<II", len(payload), len(labels)))
            parts.append(payload)
            parts.append(labels)
        return b"".join(parts)

    @property
    def size(self) -> int:
        return sum(len(payload) + len(labels) for _, _, payload, labels in self.replaced)


def build(grid: Grid, keys, epoch: int = 0, lod: str | None = None) -> Update:
    """Meshes the given chunks and compresses those that hold a surface.

    A chunk carved away as the scene changed produces no mesh and is reported for removal,
    so the client drops it rather than keeping geometry that is no longer there.
    """
    lod = lod or grid.spec.name
    update = Update(epoch=epoch)

    for key in keys:
        chunk = grid.chunk_mesh(key)
        if chunk is None:
            update.removed.append((lod, key))
        else:
            # Labels ride alongside rather than inside the compressed mesh: they are one
            # byte a vertex, so compressing them would save less than it complicates.
            update.replaced.append((lod, key, _compress(chunk), chunk.label.tobytes()))
    return update


def _compress(chunk: ChunkMesh) -> bytes:
    mesh = chunk.mesh
    colours = np.asarray(mesh.vertex_colors)
    rgba = np.empty((len(colours), 4), dtype=np.uint8)
    rgba[:, :3] = np.clip(colours * 255.0, 0, 255).astype(np.uint8)
    rgba[:, 3] = np.clip(chunk.confidence * 255.0, 0, 255).astype(np.uint8)

    return DracoPy.encode(
        np.asarray(mesh.vertices, dtype=np.float32),
        np.asarray(mesh.triangles, dtype=np.uint32),
        colors=rgba,
        quantization_bits=QUANTIZATION_BITS,
        compression_level=COMPRESSION_LEVEL,
    )
