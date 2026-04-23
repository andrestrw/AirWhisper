import sys

from faster_whisper import WhisperModel

from .base import TranscriptionEngine


class WhisperEngine(TranscriptionEngine):
    """
    Transcription engine backed by faster-whisper (CTranslate2).

    This is the original engine extracted from the monolithic file.py.
    Uses greedy decoding (beam_size=1) for maximum speed on CPU.
    """

    def __init__(
        self,
        model_size: str = "tiny",
        device: str = "cpu",
        compute_type: str = "int8",
        cpu_threads: int = 4,
    ):
        self._model_size = model_size
        self._device = device
        self._compute_type = compute_type
        self._cpu_threads = cpu_threads
        self._model: WhisperModel | None = None

    def load_model(self) -> None:
        print(
            f"[WhisperEngine] Cargando modelo '{self._model_size}'...",
            file=sys.stderr,
        )
        self._model = WhisperModel(
            self._model_size,
            device=self._device,
            compute_type=self._compute_type,
            cpu_threads=self._cpu_threads,
        )
        print("[WhisperEngine] Modelo cargado.", file=sys.stderr)

    def transcribe(self, audio_path: str, language: str = "es") -> str:
        if self._model is None:
            self.load_model()

        segmentos, _ = self._model.transcribe(  # type: ignore[union-attr]
            audio_path, beam_size=1, language=language
        )
        return "".join(s.text for s in segmentos).strip()
