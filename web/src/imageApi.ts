import { ApiRequestError, apiPath } from './api'

// Adding an image to a note from the editor (#187; src/Wolfden.Api/ImageEndpoints.cs).
// The server saves it in an `images/` folder beside the note, under a name it
// makes from the file's (never replacing a file), and answers with the path
// to write in the note, from the note's folder (format.md F-LAY-9).

/** An image the server saved (AddImageResult). */
export interface AddedImage {
  version: number
  /** Its den-relative path. */
  path: string
  /** The path to write in the note, from the note's folder, such as `images/castle-map.png`. */
  link: string
  /** Alt text made from the file's name, or empty. */
  alt: string
  bytes: number
  /** Such as a name that was taken, so the image got a number. */
  effects: string[]
}

/** The image types the den shows, and so the ones the editor adds (format.md F-LAY-9). */
export const imageExtensions = ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.avif', '.svg'] as const

/** The largest image the server takes: 10 MiB (DenTools.MaxImageBytes). */
export const maxImageBytes = 10 * 1024 * 1024

function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.')
  return dot < 0 ? '' : name.slice(dot).toLowerCase()
}

/** True if the file looks like an image: by its type, or its name's extension. */
export function looksLikeImage(file: File): boolean {
  return file.type.startsWith('image/') || (imageExtensions as readonly string[]).includes(extensionOf(file.name))
}

/** Why the file can't be added, before sending it, or null. The server checks again, its contents too. */
export function imageProblem(file: File): string | null {
  const extension = extensionOf(file.name)
  if (!(imageExtensions as readonly string[]).includes(extension)) {
    return `Wolfden adds ${imageExtensions.join(', ')} files${extension ? `, not ${extension}` : ''}.`
  }

  if (file.size > maxImageBytes) {
    return `It's ${(file.size / (1024 * 1024)).toFixed(1)} MB, and Wolfden adds images of up to 10 MB. Make it smaller in an image editor, then add it again.`
  }

  return null
}

/** Sends the file to be saved beside the note, and returns what the server saved, or throws an ApiRequestError. */
export async function addImage(universe: string, entity: string, file: File): Promise<AddedImage> {
  const response = await fetch(apiPath('/api/entity/image', { entity, universe, name: file.name }), {
    method: 'POST',
    headers: { Accept: 'application/json', 'Content-Type': 'application/octet-stream' },
    body: file,
  })
  const answer: unknown = await response.json().catch(() => undefined)
  const json = typeof answer === 'object' && answer !== null ? (answer as Record<string, unknown>) : null
  if (!response.ok) {
    throw new ApiRequestError(
      typeof json?.code === 'string' ? json.code : 'http_error',
      typeof json?.message === 'string' ? json.message : `The server answered ${response.status}.`,
    )
  }

  if (!json || typeof json.link !== 'string') {
    throw new ApiRequestError('bad_response', "The server's answer wasn't the JSON the page expects.")
  }

  return json as unknown as AddedImage
}

/** The Markdown for an added image: `![alt](link)`. The server's names have no spaces, `%`, `#`, or parentheses, so the link needs no escaping. */
export function imageMarkdown(image: Pick<AddedImage, 'alt' | 'link'>): string {
  return `![${image.alt}](${image.link})`
}
