export async function consumeNdjsonStream<T>(
  stream: ReadableStream<Uint8Array>,
  consume: (event: T) => void,
): Promise<void> {
  const reader = stream.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  while (true) {
    const { value, done } = await reader.read()
    buffer += decoder.decode(value, { stream: !done })
    const lines = buffer.split('\n')
    buffer = lines.pop() ?? ''
    for (const line of lines) {
      if (line.trim()) consume(JSON.parse(line) as T)
    }
    if (done) break
  }

  if (buffer.trim()) consume(JSON.parse(buffer) as T)
}
