import contextlib
import os
import sys

from .base import TranscriptionEngine

# Disable tqdm progress bars and ANSI color codes so stderr stays clean
# when C# captures and displays it.
os.environ.setdefault("TQDM_DISABLE", "1")
os.environ.setdefault("NO_COLOR", "1")


class UniASREngine(TranscriptionEngine):
    """
    Transcription engine backed by FunASR's UniASR model.

    Key characteristics:
    - Non-autoregressive (2-pass CTC+attention) → very fast on CPU,
      similar to SenseVoice.
    - This specific model is trained exclusively on Spanish, so it is more
      accurate for Spanish than SenseVoice's auto-detection mode.
    - Language is fixed to Spanish; passing any other language code is
      ignored by the underlying model.

    The model (~300 MB) is downloaded automatically on first run from
    ModelScope.
    """

    def __init__(
        self,
        model_id: str = "iic/speech_UniASR_asr_2pass-es-16k-common-vocab3445-tensorflow1-online",
        device: str = "cpu",
    ):
        self._model_id = model_id
        self._device = device
        self._model = None

    def load_model(self) -> None:
        print(
            f"[UniASREngine] Cargando modelo '{self._model_id}'...",
            file=sys.stderr,
        )
        # IMPORTANT: Do NOT import AutoModel until the patch is ready.

        # 1. Resolve local cache path (ModelScope convention)
        model_cache_dir = os.path.join(
            os.path.expanduser("~"), ".cache", "modelscope", "hub", "models",
            self._model_id.replace("/", os.sep),
        )

        if os.path.isdir(model_cache_dir):
            # Model already cached — skip network call entirely
            print("[UniASREngine] Modelo encontrado en caché local.", file=sys.stderr)
            model_dir = model_cache_dir
        else:
            # First run: download from ModelScope
            print("[UniASREngine] Descargando modelo desde ModelScope (solo primera vez)...", file=sys.stderr)
            from modelscope.hub.snapshot_download import snapshot_download
            with contextlib.redirect_stdout(sys.stderr):
                model_dir = snapshot_download(self._model_id)

        model_py = os.path.join(model_dir, "model.py")

        # 2. Apply Windows Patch BEFORE any funasr imports
        if os.path.isfile(model_py):
            if "model" in sys.modules:
                del sys.modules["model"]

            import importlib.util
            spec = importlib.util.spec_from_file_location("model", model_py)
            if spec and spec.loader:
                mod = importlib.util.module_from_spec(spec)
                sys.modules["model"] = mod
                spec.loader.exec_module(mod)

        # 3. Now it is safe to import and load
        from funasr import AutoModel

        with contextlib.redirect_stdout(sys.stderr):
            self._model = AutoModel(
                model=self._model_id,
                trust_remote_code=True,
                device=self._device,
                disable_update=True,
                hub="ms",
            )
        print("[UniASREngine] Modelo cargado.", file=sys.stderr)


    def transcribe(self, audio_path: str, language: str = "es") -> str:
        """
        Transcribes the given audio file.

        Note: This model is Spanish-only. The 'language' parameter is
        accepted for API compatibility but has no effect on the output.
        """
        if self._model is None:
            self.load_model()

        with contextlib.redirect_stdout(sys.stderr):
            res = self._model.generate(  # type: ignore[union-attr]
                input=audio_path,
                cache={},
                use_itn=True,
                batch_size_s=60,
            )

        if not res or not res[0].get("text"):
            return ""

        return res[0]["text"].strip()
