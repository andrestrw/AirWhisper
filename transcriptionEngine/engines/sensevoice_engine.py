import contextlib
import os
import sys

from .base import TranscriptionEngine

# Disable tqdm progress bars and ANSI color codes so stderr stays clean
# when C# captures and displays it.
os.environ.setdefault("TQDM_DISABLE", "1")
os.environ.setdefault("NO_COLOR", "1")


class SenseVoiceEngine(TranscriptionEngine):
    """
    Transcription engine backed by FunASR's SenseVoiceSmall model.

    Key characteristics:
    - Non-autoregressive → near-instant inference (5-15× faster than Whisper).
    - Includes emotion/event detection tags in raw output; these are stripped
      by ``rich_transcription_postprocess``.
    - Officially supports: zh, en, ja, ko, yue.  Spanish ("es") is NOT an
      officially supported language, but "auto" mode may produce usable results.

    The model (~500 MB) is downloaded automatically on first run from
    ModelScope / HuggingFace.
    """

    def __init__(
        self,
        model_id: str = "iic/SenseVoiceSmall",
        device: str = "cpu",
    ):
        self._model_id = model_id
        self._device = device
        self._model = None

    def load_model(self) -> None:
        print(
            f"[SenseVoiceEngine] Cargando modelo '{self._model_id}'...",
            file=sys.stderr,
        )
        # FunASR downloads the model's custom inference code (model.py) to the
        # ModelScope cache dir but never registers it in sys.modules, so
        # trust_remote_code=True fails with "No module named 'model'" on Windows.
        # Pre-register it via importlib so the subsequent import inside AutoModel
        # finds it without requiring sys.path surgery.
        model_cache_dir = os.path.join(
            os.path.expanduser("~"), ".cache", "modelscope", "hub", "models",
            self._model_id.replace("/", os.sep),
        )
        model_py = os.path.join(model_cache_dir, "model.py")
        if os.path.isfile(model_py) and "model" not in sys.modules:
            import importlib.util
            spec = importlib.util.spec_from_file_location("model", model_py)
            if spec and spec.loader:
                mod = importlib.util.module_from_spec(spec)
                sys.modules["model"] = mod
                spec.loader.exec_module(mod)  # type: ignore[union-attr]

        # Import here so funasr is only loaded when this engine is selected.
        # This avoids penalising startup time when using Whisper.
        from funasr import AutoModel

        # funasr prints version info and download progress to stdout;
        # redirect to stderr so C# only reads the final transcription from stdout.
        with contextlib.redirect_stdout(sys.stderr):
            self._model = AutoModel(
                model=self._model_id,
                trust_remote_code=True,
                device=self._device,
                disable_update=True,    # Skip version check (avoids network call)
                # FSMN-VAD automatically segments long audio into chunks
                vad_model="fsmn-vad",
                vad_kwargs={"max_single_segment_time": 30000},
            )
        print("[SenseVoiceEngine] Modelo cargado.", file=sys.stderr)

    def transcribe(self, audio_path: str, language: str = "auto") -> str:
        if self._model is None:
            self.load_model()

        from funasr.utils.postprocess_utils import rich_transcription_postprocess

        with contextlib.redirect_stdout(sys.stderr):
            res = self._model.generate(  # type: ignore[union-attr]
                input=audio_path,
                cache={},
                language=language,
                use_itn=True,
                batch_size_s=60,
                merge_vad=True,
                merge_length_s=15,
            )

        # rich_transcription_postprocess strips emotion/event tags like
        # <|HAPPY|>, <|APPLAUSE|>, etc. leaving only the clean text.
        text = rich_transcription_postprocess(res[0]["text"])
        return text.strip()
