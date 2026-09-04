import os
from pathlib import Path

import folder_paths
from aiohttp import web
from server import PromptServer


@PromptServer.instance.routes.post("/upload/audio")
async def upload_audio(request):
    form = await request.post()
    upload = form.get("audio")
    if upload is None or not getattr(upload, "filename", None):
        return web.json_response({"error": "audio file is required"}, status=400)

    file_name = Path(upload.filename).name
    if Path(file_name).suffix.lower() not in {".wav", ".mp3", ".m4a", ".ogg", ".flac"}:
        return web.json_response({"error": "unsupported audio format"}, status=400)

    input_directory = Path(folder_paths.get_input_directory()).resolve()
    output_path = (input_directory / file_name).resolve()
    if os.path.commonpath([input_directory, output_path]) != str(input_directory):
        return web.json_response({"error": "invalid file name"}, status=400)
    if output_path.exists() and form.get("overwrite") != "true":
        return web.json_response({"error": "file already exists"}, status=409)

    output_path.write_bytes(upload.file.read())
    return web.json_response({"name": file_name, "subfolder": "", "type": "input"})


NODE_CLASS_MAPPINGS = {}
NODE_DISPLAY_NAME_MAPPINGS = {}