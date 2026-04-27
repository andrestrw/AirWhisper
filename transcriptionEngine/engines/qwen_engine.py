import sys
from .base import TranscriptionEngine

class QwenEngine(TranscriptionEngine):
    """
    Transcription engine backed by Qwen3-ASR-0.6B.
    
    This is a lightweight, high-performance model from the Qwen3 family.
    Optimized for speed and supports 50+ languages.
    """

    def __init__(
        self,
        model_id: str = "Qwen/Qwen3-ASR-0.6B",
        device: str = "cpu",
    ):
        self._model_id = model_id
        self._device = device
        self._model = None

    def load_model(self) -> None:
        print(
            f"[QwenEngine] Cargando modelo '{self._model_id}'...",
            file=sys.stderr,
        )
        # Import inside load_model to avoid unnecessary overhead if not used
        try:
            from qwen_asr import Qwen3ASRModel
        except ImportError:
            print(
                "\nERROR: 'qwen-asr' no está instalado correctamente.\n"
                "Por favor, ejecuta: pip install -U qwen-asr\n",
                file=sys.stderr
            )
            sys.exit(1)

        self._model = Qwen3ASRModel.from_pretrained(
            self._model_id,
            device_map=self._device,
        )

        print("[QwenEngine] Modelo cargado.", file=sys.stderr)

    def transcribe(self, audio_path: str, language: str = "es") -> str:
        if self._model is None:
            self.load_model()

        # Qwen3ASRModel expects 'audio' parameter for the path
        # and returns a list of results.
        results = self._model.transcribe(
            audio=audio_path,
        )
        
        if not results or len(results) == 0:
            return ""

        # Extract text from the first result object
        return results[0].text.strip()

