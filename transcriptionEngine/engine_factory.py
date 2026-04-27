"""
Factory module that maps engine names to their concrete implementations.

Imports are deferred inside each branch so that only the dependencies of the
selected engine are loaded (e.g. selecting "whisper" never imports funasr).
"""

from engines.base import TranscriptionEngine


# Registry of supported engine names — used for argparse choices and error messages.
AVAILABLE_ENGINES = ("whisper", "sensevoice", "qwen", "uniasr")



def create_engine(engine_name: str) -> TranscriptionEngine:
    """
    Instantiate the correct transcription engine.

    Args:
        engine_name: One of AVAILABLE_ENGINES.

    Returns:
        A ready-to-use TranscriptionEngine (model not yet loaded).

    Raises:
        ValueError: If engine_name is not recognised.
    """
    if engine_name == "whisper":
        from engines.whisper_engine import WhisperEngine

        return WhisperEngine(model_size="tiny", device="cpu")

    if engine_name == "sensevoice":
        from engines.sensevoice_engine import SenseVoiceEngine

        return SenseVoiceEngine(model_id="iic/SenseVoiceSmall", device="cpu")

    if engine_name == "qwen":
        from engines.qwen_engine import QwenEngine

        return QwenEngine(model_id="Qwen/Qwen3-ASR-0.6B", device="cpu")

    if engine_name == "uniasr":
        from engines.uniasr_engine import UniASREngine

        return UniASREngine(
            model_id="iic/speech_UniASR_asr_2pass-es-16k-common-vocab3445-tensorflow1-online",
            device="cpu",
        )
    raise ValueError(
        f"Motor desconocido: '{engine_name}'. "
        f"Opciones válidas: {', '.join(AVAILABLE_ENGINES)}"
    )
