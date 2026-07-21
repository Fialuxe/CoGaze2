# CoGaze2

Remote collaboration for hands-on physical work. A **worker** wearing a Quest 3S, an **expert**
at a PC with a webcam, and a **GPU server** between them. The worker's space is reconstructed in
3D so the expert can look around it from any viewpoint, and the expert's gaze is drawn back into
the worker's mixed-reality view. The research question is gaze deixis and joint attention from a
plain webcam.

Last updated: 2026-07-21

---

## English

### Status

The environment line — worker's stereo cameras to a mesh the expert can look at, in the worker's
own coordinate frame — works end to end, as a batch over recordings and as a live WebSocket
service. The gaze work, which is the actual contribution, is not started.

### Layout

- `server/` — Python pipeline: `rectify` → `depth/` (stereo estimator by name) → `fusion`
  (two-grid GPU TSDF) → `chunks` (Draco-compressed changed chunks). Batch scripts and the live
  `service.py`.
- `Assets/Scripts/EnvLine/` — Unity: worker capture (`KeyframeSource`, `KeyframeUploader`) and
  expert display (`MeshReceiver`).
- `docs/` — `next_steps.md` is the authoritative account of what exists and why; `server_contract.md`
  fixes the wire boundary; `geometry_validation.md` holds the camera-model measurements.
- `CLAUDE.md` — working notes and conventions.
- `third_party/` — vendored stereo matchers (weights not tracked).

### Running

Batch, over a recording:

```
cd server
python run_fusion.py ../recordings/session01                 # whole meshes
python run_chunks.py ../recordings/session01 --out ../recordings/session01_chunks
```

Live:

```
cd server
python service.py --estimator fast --scale 0.5               # worker in on /worker, expert out on /expert
python replay_worker.py --limit 8                            # feed a recording as the worker (no headset)
# Unity: MeshReceiver with serverUrl = ws://127.0.0.1:8765/expert
```

### Notes

- Depth estimators: `foundation` (accurate, ~0.2 Hz), `fast` (Fast-FoundationStereo, ~2.6 Hz
  full-res / ~10 Hz at `--scale 0.5`, same coverage), `sgbm` (baseline, holes on textureless).
- Everything crossing the server is OpenCV convention (right-handed, +Y down, +Z forward).
- Over Quest Link the passthrough camera returns colour bars, not images — capture needs an
  on-device build.

---

## 日本語

### 状況

環境ライン（作業者のステレオカメラ → 専門家が見るメッシュ、作業者自身の座標系のまま）は、録画に対するバッチと、ライブの WebSocket サービスの両方で端から端まで動く。本題である視線（gaze）はまだ未着手。

### 構成

- `server/` — Python パイプライン：`rectify` → `depth/`（名前で選ぶステレオ推定）→ `fusion`
  （2グリッドの GPU TSDF）→ `chunks`（Draco 圧縮した差分チャンク）。バッチ各種とライブ `service.py`。
- `Assets/Scripts/EnvLine/` — Unity：作業者側の撮影（`KeyframeSource`, `KeyframeUploader`）と
  専門家側の表示（`MeshReceiver`）。
- `docs/` — `next_steps.md` が「何があり、なぜそうしたか」の正典。`server_contract.md` が境界仕様、
  `geometry_validation.md` がカメラモデルの計測値。
- `CLAUDE.md` — 作業メモと規約。
- `third_party/` — ベンダリングしたステレオ手法（重みは追跡しない）。

### 実行

バッチ（録画に対して）:

```
cd server
python run_fusion.py ../recordings/session01                 # メッシュ全体
python run_chunks.py ../recordings/session01 --out ../recordings/session01_chunks
```

ライブ:

```
cd server
python service.py --estimator fast --scale 0.5               # /worker で受信、/expert へ配信
python replay_worker.py --limit 8                            # 録画を worker として流す（ヘッドセット不要）
# Unity: MeshReceiver の serverUrl = ws://127.0.0.1:8765/expert
```

### 補足

- 深度推定：`foundation`（高精度・約0.2Hz）、`fast`（Fast-FoundationStereo、フル約2.6Hz /
  `--scale 0.5` で約10Hz、カバレッジ同等）、`sgbm`（ベースライン、無テクスチャ面に穴）。
- サーバ境界は全て OpenCV 規約（右手系、+Y 下、+Z 前方）。
- Quest Link 経由ではパススルーがカラーバーを返す（実画像ではない）ため、撮影は実機ビルドが必要。
