"""
Multi-engine audio transcription entry point.

Two modes of operation:

  Single-shot (default):
      python main.py <audio.wav> [--engine whisper|sensevoice] [--language es]
      Transcribes one file, prints result to stdout, exits.

  Daemon mode (used by C# El Conserje):
      python main.py --engine sensevoice --daemon
      Loads model once, then reads audio paths from stdin line by line.
      For each path it prints the transcription followed by <<<DONE>>> so
      C# knows when the result is complete.

stdout contract (both modes):
    • stdout  → clean transcription text only (C# reads this)
    • stderr  → progress / debug logs (C# displays these as grey text)
"""

import os
import sys
import time
import argparse

os.environ["OMP_NUM_THREADS"] = "4"

# Force UTF-8 on stdout/stderr so non-ASCII characters (accents, CJK, etc.)
# don't crash when Windows console defaults to cp1252.
sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")

import warnings
warnings.filterwarnings("ignore")
import logging
logging.disable(logging.CRITICAL)

_DEFAULT_LANGUAGE = {
    "whisper": "es",
    "sensevoice": "auto",
    "qwen": "es",
}

_DONE_SENTINEL = "<<<DONE>>>"


def _transcribe_one(engine, audio_path: str, language: str) -> str:
    if not os.path.isfile(audio_path):
        print(f"[Brain] Audio file not found: {audio_path}", file=sys.stderr, flush=True)
        return ""
    print("Iniciando transcripción...", file=sys.stderr, flush=True)
    start = time.time()
    texto = engine.transcribe(audio_path, language=language)
    print(f"Transcripción completada en {time.time() - start:.2f}s", file=sys.stderr, flush=True)
    return texto


def run_daemon(engine, language: str) -> None:
    """
    Persistent loop: reads one audio path per line from stdin, writes the
    transcription (+ <<<DONE>>> sentinel) to stdout.  Model stays in memory.
    """
    print("READY", flush=True)  # signal to C# that the model is loaded

    for raw in sys.stdin:
        audio_path = raw.strip()
        if not audio_path:
            continue
        if audio_path == "QUIT":
            break
        texto = _transcribe_one(engine, audio_path, language)
        print(texto, flush=True)
        print(_DONE_SENTINEL, flush=True)


def main() -> None:
    from engine_factory import create_engine, AVAILABLE_ENGINES

    parser = argparse.ArgumentParser(
        description="Motor de transcripción de audio multi-modelo",
    )
    parser.add_argument(
        "audio_path",
        nargs="?",
        help="Ruta al archivo de audio .wav (omitir en modo --daemon)",
    )
    parser.add_argument(
        "--engine",
        choices=AVAILABLE_ENGINES,
        default="whisper",
        help="Motor de transcripción a usar (default: whisper)",
    )
    parser.add_argument(
        "--language",
        default=None,
        help="Código de idioma (ej: 'es', 'en', 'auto'). "
             "Default: 'es' para whisper, 'auto' para sensevoice",
    )
    parser.add_argument(
        "--daemon",
        action="store_true",
        help="Modo persistente: mantiene el modelo en memoria y lee rutas de audio desde stdin",
    )

    args = parser.parse_args()

    if not args.daemon and not args.audio_path:
        parser.error("audio_path es obligatorio en modo single-shot (omite --daemon)")

    engine = create_engine(args.engine)
    language = args.language or _DEFAULT_LANGUAGE.get(args.engine, "es")
    engine.load_model()

    if args.daemon:
        run_daemon(engine, language)
    else:
        texto = _transcribe_one(engine, args.audio_path, language)
        print(texto)


if __name__ == "__main__":
    main()
