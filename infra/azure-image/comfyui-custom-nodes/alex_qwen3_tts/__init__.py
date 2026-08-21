from __future__ import annotations

import io
import wave

import numpy as np
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


NODE_CLASS_MAPPINGS = {"AlexQwen3TTS": AlexQwen3TTS}
NODE_DISPLAY_NAME_MAPPINGS = {"AlexQwen3TTS": "Alex Qwen3 TTS"}