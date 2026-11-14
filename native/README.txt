Third-party components used by PixTune (unmodified):

libraw.dll - LibRaw 0.22.3 (https://www.libraw.org), official Windows x64 build from
  https://www.libraw.org/data/LibRaw-0.22.3-Win64.zip (SHA-256 of the zip 4E2213A0B10E32199A30B9114A61376CDB40F94DF861262B4A6DCA659701276B).
  Opens camera RAW files. Dual-licensed LGPL 2.1 / CDDL 1.0 (LibRaw-LICENSE.*.txt, LibRaw-COPYRIGHT.txt);
  it can be replaced by any later libraw.dll with the same C interface. Needs the Microsoft Visual C++ 2015-2022
  runtime (x64): https://aka.ms/vs/17/release/vc_redist.x64.exe

onnxruntime.dll - ONNX Runtime 1.24.4 with DirectML, by Microsoft (MIT licence, OnnxRuntime-LICENSE.txt), from the
  NuGet package Microsoft.ML.OnnxRuntime.DirectML 1.24.4. Runs the AI models (Head Pose here; the optional
  downloads of healing, expansion, masks and denoise use it too).

face_landmark_detector.onnx / .data - MediaPipe Face Mesh landmark model by Google (Apache License 2.0,
  MediaPipe-LICENSE.txt), ONNX export by Qualcomm AI Hub (qai-hub-models v0.65.0, mediapipe_face). 468 points
  with depth on a face, used by Head Pose. The face mesh triangles in engine\FaceMesh.cs come from MediaPipe's
  canonical_face_model.obj (Apache 2.0).

liveportrait_mask.png - the soft face mask (assets/mask_template.png) from LivePortrait by Kuaishou
  (https://github.com/KwaiVGI/LivePortrait, MIT licence, LivePortrait-LICENSE.txt). Used by the AI mode of
  Head Pose to paste the redrawn face back; the LivePortrait models themselves are downloaded on first use.
