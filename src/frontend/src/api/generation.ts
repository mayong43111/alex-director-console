export interface GenerationAssetReference {
  assetId: string
  resourceId: string
  version: number
  name: string
  type: string
  role: string
  contentUrl: string | null
}

export interface ImageGenerationParameters {
  deployment: string
  quality: string
  modelSize: string
  outputFormat: string
  outputWidth: number
  outputHeight: number
  productionMode: string | null
  durationSeconds: number | null
  stages: string[] | null
}

export interface ImageGenerationPreview {
  operation: string
  prompt: string
  parameters: ImageGenerationParameters
  references: GenerationAssetReference[]
}