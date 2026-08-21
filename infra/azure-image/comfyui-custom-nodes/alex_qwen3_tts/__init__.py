from __future__ import annotations

import io
import os
import wave

import numpy as np
import folder_paths
import requests
import torch


class AlexQwen3TTS:
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "text": ("STRING", {"multiline": True}),
                "design_prompt": ("STRING", {"multiline": True}),
                "language": (["Chinese", "English", "Japanese", "Korean"],),
                "seed": ("INT", {"default": 20260821, "min": 0, "max": 2**31 - 1}),
            }
        }

    RETURN_TYPES = ("AUDIO",)
    RETURN_NAMES = ("audio",)
    FUNCTION = "generate"
    CATEGORY = "Alex/Audio"

    def generate(self, text: str, design_prompt: str, language: str, seed: int):
        response = requests.post(
            "http://127.0.0.1:8010/v1/voice-design",
            json={
                "text": text,
                "designPrompt": design_prompt,
                "language": language,
                "seed": seed,
            },
            timeout=1800,
        )
        response.raise_for_status()
        with wave.open(io.BytesIO(response.content), "rb") as wav:
            if wav.getsampwidth() != 2:
                raise ValueError("Qwen3-TTS must return PCM 16-bit WAV audio")
            sample_rate = wav.getframerate()
            channels = wav.getnchannels()
            samples = np.frombuffer(wav.readframes(wav.getnframes()), dtype="<i2")
        audio = torch.from_numpy(samples.copy()).float().div_(32768.0)
        audio = audio.reshape(-1, channels).transpose(0, 1)
        return ({"waveform": audio.unsqueeze(0), "sample_rate": sample_rate},)


class AlexSaveAudioWav:
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "audio": ("AUDIO",),
                "filename_prefix": ("STRING", {"default": "alex-tts/dialogue"}),
            }
        }

    RETURN_TYPES = ()
    FUNCTION = "save"
    OUTPUT_NODE = True
    CATEGORY = "Alex/Audio"

    def save(self, audio, filename_prefix: str):
        output_dir, filename, counter, subfolder, _ = folder_paths.get_save_image_path(
            filename_prefix,
            folder_paths.get_output_directory(),
        )
        waveform = audio["waveform"][0].detach().cpu().clamp(-1, 1)
        samples = (waveform.transpose(0, 1).numpy() * 32767.0).astype("<i2")
        file_name = f"{filename}_{counter:05}_.wav"
        path = os.path.join(output_dir, file_name)
        with wave.open(path, "wb") as wav:
            wav.setnchannels(samples.shape[1])
            wav.setsampwidth(2)
            wav.setframerate(int(audio["sample_rate"]))
            wav.writeframes(samples.tobytes())
        return {
            "ui": {
                "audio": [
                    {"filename": file_name, "subfolder": subfolder, "type": "output"}
                ]
            }
        }


NODE_CLASS_MAPPINGS = {
    "AlexQwen3TTS": AlexQwen3TTS,
    "AlexSaveAudioWav": AlexSaveAudioWav,
}
NODE_DISPLAY_NAME_MAPPINGS = {
    "AlexQwen3TTS": "Alex Qwen3 TTS",
    "AlexSaveAudioWav": "Alex Save Audio WAV",
}