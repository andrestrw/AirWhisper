import os
import sys
import time

os.environ["OMP_NUM_THREADS"] = "4"

# Suppress noisy warnings from underlying libraries (CTranslate2, PyTorch, etc.)
# so that stdout stays clean for C# to capture.
import warnings
warnings.filterwarnings("ignore")
import logging
logging.disable(logging.CRITICAL)

from faster_whisper import WhisperModel

# --- Validate input ---
if len(sys.argv) < 2:
    print("Error: No se proporcionó la ruta del archivo de audio.", file=sys.stderr)
    print("Uso: python file.py <ruta_audio.wav>", file=sys.stderr)
    sys.exit(1)

audio_path = sys.argv[1]

if not os.path.isfile(audio_path):
    print(f"Error: El archivo de audio no existe: {audio_path}", file=sys.stderr)
    sys.exit(1)

# --- Load model ---
print("Cargando modelo 'tiny'...", file=sys.stderr)
modelo = WhisperModel("tiny", device="cpu", compute_type="int8", cpu_threads=4)
print("Modelo cargado.", file=sys.stderr)

# --- Transcribe ---
print("Iniciando transcripción...", file=sys.stderr)
start_time = time.time()

# beam_size=1: fastest greedy decoding
# language="en": skip language detection overhead
segmentos, info = modelo.transcribe(audio_path, beam_size=1, language="es")
texto_completo = "".join([segmento.text for segmento in segmentos])

elapsed_time = time.time() - start_time
print(f"Transcripción completada en {elapsed_time:.2f}s", file=sys.stderr)

# === ÚNICA SALIDA A STDOUT: el texto limpio de la transcripción ===
print(texto_completo.strip())
