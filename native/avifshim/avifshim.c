/* avifshim: decodes the first frame of an AVIF picture held in memory to rows of
 * 32-bit pixels, blue, green, red and premultiplied alpha, at a size the caller
 * picks from the picture's own; libavif reads the file, dav1d decodes AV1. */
#include <stddef.h>
#include <stdint.h>

#include "avif/avif.h"

#define AVS_API __attribute__((visibility("default")))

static avifDecoder *avs_parse(const uint8_t *data, size_t size) {
  avifDecoder *decoder = avifDecoderCreate();
  if (!decoder) return NULL;
  /* Browsers show pictures that bend the specification, so the window does too. */
  decoder->strictFlags = AVIF_STRICT_DISABLED;
  decoder->ignoreExif = AVIF_TRUE;
  decoder->ignoreXMP = AVIF_TRUE;
  if (avifDecoderSetIOMemory(decoder, data, size) != AVIF_RESULT_OK || avifDecoderParse(decoder) != AVIF_RESULT_OK) {
    avifDecoderDestroy(decoder);
    return NULL;
  }
  return decoder;
}

/* Reads the picture's own size; returns 0, or 1 when it is no AVIF or damaged. */
AVS_API int avs_size(const uint8_t *data, size_t size, int32_t *width, int32_t *height) {
  avifDecoder *decoder = avs_parse(data, size);
  if (!decoder) return 1;
  *width = (int32_t)decoder->image->width;
  *height = (int32_t)decoder->image->height;
  avifDecoderDestroy(decoder);
  return 0;
}

/* Decodes the picture scaled to width x height into pixels, which hold
 * width x height x 4 bytes; returns 0, or 1 when it cannot be decoded. */
AVS_API int avs_decode(const uint8_t *data, size_t size, int32_t width, int32_t height, uint8_t *pixels) {
  avifDecoder *decoder = avs_parse(data, size);
  avifRGBImage rgb;
  int failed;
  if (!decoder) return 1;
  failed = width <= 0 || height <= 0 || avifDecoderNextImage(decoder) != AVIF_RESULT_OK
           || avifImageScale(decoder->image, (uint32_t)width, (uint32_t)height, &decoder->diag) != AVIF_RESULT_OK;
  if (!failed) {
    avifRGBImageSetDefaults(&rgb, decoder->image);
    rgb.format = AVIF_RGB_FORMAT_BGRA;
    rgb.depth = 8;
    rgb.alphaPremultiplied = AVIF_TRUE;
    rgb.pixels = pixels;
    rgb.rowBytes = (uint32_t)width * 4;
    failed = avifImageYUVToRGB(decoder->image, &rgb) != AVIF_RESULT_OK;
  }
  avifDecoderDestroy(decoder);
  return failed;
}
