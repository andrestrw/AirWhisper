from abc import ABC, abstractmethod


class TranscriptionEngine(ABC):
    """
    Abstract contract that every transcription engine must implement.

    This follows the Strategy pattern: each concrete engine encapsulates
    a different model/library but exposes the same interface so the caller
    (main.py / C# process) never needs to know which engine is running.
    """

    @abstractmethod
    def load_model(self) -> None:
        """Load the model into memory. Called once before transcribing."""
        ...

    @abstractmethod
    def transcribe(self, audio_path: str, language: str = "en   ") -> str:
        """
        Transcribe an audio file and return the resulting text.

        Args:
            audio_path: Absolute path to the .wav file.
            language: Language code (e.g. "es", "en", "auto").

        Returns:
            Clean transcribed text.
        """
        ...
