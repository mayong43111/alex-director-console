import { FFmpeg } from '@ffmpeg/ffmpeg'
import { fetchFile } from '@ffmpeg/util'
import coreUrl from '@ffmpeg/core?url'
import wasmUrl from '@ffmpeg/core/wasm?url'

export interface EpisodeVideoClip {
  url: string
}

export async function exportEpisodeVideo(
  clips: EpisodeVideoClip[],
  onProgress: (progress: number) => void,
) {
  if (clips.length === 0) throw new Error('当前剧集还没有可导出的视频。')

  const ffmpeg = new FFmpeg()
  ffmpeg.on('progress', ({ progress }) => onProgress(45 + Math.round(progress * 55)))

  try {
    onProgress(2)
    await ffmpeg.load({ coreURL: coreUrl, wasmURL: wasmUrl })
    const clipNames: string[] = []

    for (const [index, clip] of clips.entries()) {
      const clipName = `${String(index + 1).padStart(4, '0')}.mp4`
      await ffmpeg.writeFile(clipName, await fetchFile(clip.url))
      clipNames.push(clipName)
      onProgress(5 + Math.round(((index + 1) / clips.length) * 35))
    }

    const manifest = clipNames.map((name) => `file '${name}'`).join('\n')
    await ffmpeg.writeFile('concat.txt', new TextEncoder().encode(`${manifest}\n`))
    const exitCode = await ffmpeg.exec([
      '-f', 'concat',
      '-safe', '0',
      '-i', 'concat.txt',
      '-c', 'copy',
      '-movflags', '+faststart',
      'episode.mp4',
    ])
    if (exitCode !== 0) throw new Error('视频拼接失败，请确认所有镜头视频格式一致。')

    const output = await ffmpeg.readFile('episode.mp4')
    if (typeof output === 'string') throw new Error('成片导出结果格式无效。')
    const blob = new Blob([new Uint8Array(output)], { type: 'video/mp4' })
    onProgress(100)
    return blob
  } finally {
    ffmpeg.terminate()
  }
}