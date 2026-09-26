/* xd3shim: a small streaming C ABI over the official xdelta3 library.
 *
 * The caller drives a pull loop (xds_input / xds_step) instead of receiving
 * callbacks, so the managed side needs no reverse P/Invoke and stays
 * NativeAOT-friendly. Output pointers stay valid until the next xds_step. */
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "xdelta3.h"

#if defined(_WIN32)
#define XDS_API __declspec(dllexport)
#else
#define XDS_API __attribute__((visibility("default")))
#endif

#define XDS_NEED_INPUT 1
#define XDS_HAVE_OUTPUT 2
#define XDS_DONE 3
#define XDS_NEED_SOURCE 4

#define XDS_SEC_NONE 0
#define XDS_SEC_DJW 1
#define XDS_SEC_LZMA 2

#define XDS_F_NO_CHECKSUM 1u

typedef struct xds_encode_opts {
  uint32_t struct_size;
  int32_t level;
  int32_t secondary;
  uint32_t flags;
  uint64_t winsize;
  uint64_t src_winsize;
  uint64_t src_blksize;
} xds_encode_opts;

typedef struct xds {
  xd3_stream stream;
  xd3_source source;
  const uint8_t *src;
  uint64_t src_len;
  int encoder;
  int pull;
  int final_input;
  int have_output;
  int done;
  int failed;
  volatile int cancel;
  uint8_t empty;
} xds;

static int xds_getblk(xd3_stream *stream, xd3_source *source, xoff_t blkno) {
  xds *s = (xds *)source->ioh;
  uint64_t off = (uint64_t)blkno * (uint64_t)source->blksize;
  uint64_t rem;
  /* Indexing a multi-gigabyte source happens inside a single xd3 call, so
   * this is the only place a cancellation request can interrupt it. */
  if (s->cancel) {
    stream->msg = "cancelled";
    return XD3_INTERNAL;
  }
  if (off > s->src_len) {
    stream->msg = "source block beyond end of source";
    return XD3_INVALID_INPUT;
  }
  rem = s->src_len - off;
  source->curblkno = blkno;
  source->curblk = s->src + off;
  source->onblk = (usize_t)(rem < source->blksize ? rem : source->blksize);
  return 0;
}

static uint64_t pow2_roundup(uint64_t x) {
  uint64_t p = 1;
  while (p < x) p <<= 1;
  return p;
}

static int xds_attach_source(xds *s, const uint8_t *src, uint64_t src_len,
                             uint64_t blksize, uint64_t max_winsize) {
  if (src_len == 0 || (src == NULL && !s->pull)) return 0;
  s->src = src;
  s->src_len = src_len;
  if (max_winsize == 0 || max_winsize > pow2_roundup(src_len)) max_winsize = pow2_roundup(src_len);
  max_winsize = pow2_roundup(max_winsize);
  /* The encoder advances its source window in whole blocks and rejects copies
   * further back than max_winsize, so blocks must stay much smaller than the
   * window (the xdelta3 CLI uses -B / 32); otherwise matching collapses. */
  if (blksize == 0) blksize = (uint64_t)1 << 26;
  if (blksize > max_winsize / 32) blksize = max_winsize / 32;
  if (blksize < XD3_ALLOCSIZE) blksize = XD3_ALLOCSIZE;
  s->source.blksize = (usize_t)pow2_roundup(blksize);
  s->source.name = "source";
  s->source.ioh = s;
  s->source.max_winsize = max_winsize;
  s->source.curblk = NULL;
  return xd3_set_source_and_size(&s->stream, &s->source, (xoff_t)src_len);
}

XDS_API const char *xds_version(void) { return "xd3shim 1 / xdelta3 " XDS_XD3_VERSION; }

XDS_API int xds_encoder_new(const uint8_t *src, uint64_t src_len,
                            const xds_encode_opts *o, xds **out) {
  xd3_config config;
  xds *s;
  int ret;
  uint32_t flags = 0;
  *out = NULL;
  if (o == NULL || o->struct_size < sizeof(xds_encode_opts)) return XD3_INVALID;
  if (o->level < 0 || o->level > 9) return XD3_INVALID;
  if (o->winsize > XD3_HARDMAXWINSIZE) return XD3_INVALID;
  s = (xds *)calloc(1, sizeof(xds));
  if (s == NULL) return ENOMEM;
  s->encoder = 1;

  xd3_init_config(&config, 0);
  config.winsize = (usize_t)(o->winsize ? o->winsize : XD3_DEFAULT_WINSIZE);
  config.sprevsz = XD3_DEFAULT_SPREVSZ;
  config.iopt_size = XD3_DEFAULT_IOPT_SIZE;
  config.getblk = xds_getblk;

  if (!(o->flags & XDS_F_NO_CHECKSUM)) flags |= XD3_ADLER32;
  /* Same level to string-matcher mapping as xdelta3-main.h, so patches match
   * what translators get from `xdelta3 -e -N`. */
  if (o->level == 0) {
    flags |= XD3_NOCOMPRESS;
    config.smatch_cfg = XD3_SMATCH_FASTEST;
  } else if (o->level == 1) config.smatch_cfg = XD3_SMATCH_FASTEST;
  else if (o->level == 2) config.smatch_cfg = XD3_SMATCH_FASTER;
  else if (o->level <= 5) config.smatch_cfg = XD3_SMATCH_FAST;
  else if (o->level == 6) config.smatch_cfg = XD3_SMATCH_DEFAULT;
  else config.smatch_cfg = XD3_SMATCH_SLOW;

  switch (o->secondary) {
  case XDS_SEC_NONE: break;
  case XDS_SEC_DJW:
    flags |= XD3_SEC_DJW;
    config.sec_data.ngroups = 1;
    config.sec_inst.ngroups = 1;
    config.sec_addr.ngroups = 1;
    break;
  case XDS_SEC_LZMA:
    flags |= XD3_SEC_LZMA;
    break;
  default:
    free(s);
    return XD3_INVALID;
  }
  config.flags = flags;

  if ((ret = xd3_config_stream(&s->stream, &config)) != 0 ||
      (ret = xds_attach_source(s, src, src_len, o->src_blksize, o->src_winsize)) != 0) {
    xd3_free_stream(&s->stream);
    free(s);
    return ret;
  }
  *out = s;
  return 0;
}

/* src == NULL with src_len > 0 selects pull mode: the caller supplies source
 * blocks on XDS_NEED_SOURCE. Reading through the caller turns I/O errors into
 * ordinary exceptions instead of access violations on a memory-mapped view. */
XDS_API int xds_decoder_new(const uint8_t *src, uint64_t src_len, uint32_t flags,
                            uint64_t blksize, xds **out) {
  xd3_config config;
  xds *s;
  int ret;
  *out = NULL;
  s = (xds *)calloc(1, sizeof(xds));
  if (s == NULL) return ENOMEM;
  xd3_init_config(&config, (flags & XDS_F_NO_CHECKSUM) ? XD3_ADLER32_NOVER : 0);
  config.winsize = XD3_DEFAULT_WINSIZE;
  s->pull = (src == NULL && src_len > 0);
  config.getblk = s->pull ? NULL : xds_getblk;
  if (s->pull && blksize == 0) blksize = (uint64_t)1 << 23;
  if ((ret = xd3_config_stream(&s->stream, &config)) != 0 ||
      (ret = xds_attach_source(s, src, src_len, blksize, 0)) != 0) {
    xd3_free_stream(&s->stream);
    free(s);
    return ret;
  }
  *out = s;
  return 0;
}

/* The encoder must receive whole windows except for the last chunk. `buf`
 * must stay valid until xds_step asks for more input. */
XDS_API int xds_input(xds *s, const uint8_t *buf, uint64_t len, int32_t final) {
  if (s->failed || s->done) return XD3_INTERNAL;
  if (final) {
    s->final_input = 1;
    if (s->encoder) {
      /* Like xdelta3-main: an empty read after at least one window ends the
       * stream without emitting an empty trailing window. */
      if (len == 0 && s->stream.current_window > 0 && s->stream.buf_leftover == NULL) {
        s->done = 2;
        return 0;
      }
      xd3_set_flags(&s->stream, s->stream.flags | XD3_FLUSH);
    }
  }
  xd3_avail_input(&s->stream, buf ? buf : &s->empty, (usize_t)len);
  return 0;
}

XDS_API int xds_step(xds *s, const uint8_t **out, uint64_t *out_len) {
  int ret;
  *out = NULL;
  *out_len = 0;
  if (s->failed) return XD3_INTERNAL;
  if (s->cancel) {
    s->failed = 1;
    return XD3_INTERNAL;
  }
  if (s->have_output) {
    xd3_consume_output(&s->stream);
    s->have_output = 0;
  }
  if (s->done == 1) return XDS_DONE;
  if (s->done == 2) goto finish;
  for (;;) {
    ret = s->encoder ? xd3_encode_input(&s->stream) : xd3_decode_input(&s->stream);
    switch (ret) {
    case XD3_OUTPUT:
      *out = s->stream.next_out;
      *out_len = s->stream.avail_out;
      s->have_output = 1;
      return XDS_HAVE_OUTPUT;
    case XD3_INPUT:
      if (!s->final_input) return XDS_NEED_INPUT;
      goto finish;
    case XD3_GETSRCBLK:
      if (s->pull) return XDS_NEED_SOURCE;
      s->failed = 1;
      return XD3_INTERNAL;
    case XD3_GOTHEADER:
    case XD3_WINSTART:
    case XD3_WINFINISH:
      continue;
    default:
      s->failed = 1;
      return ret;
    }
  }
finish:
  /* xd3_close_stream accepts an empty input, which would "decode" to an empty
   * file; a real patch always has a VCDIFF header. */
  if (!s->encoder && s->stream.dec_state == DEC_VCHEAD) {
    s->stream.msg = "empty patch (no VCDIFF header)";
    s->failed = 1;
    return XD3_INVALID_INPUT;
  }
  /* A patch truncated exactly on a window boundary still passes this check,
   * so callers must verify the target length and hash. */
  if ((ret = xd3_close_stream(&s->stream)) != 0) {
    s->failed = 1;
    return ret;
  }
  s->done = 1;
  return XDS_DONE;
}

XDS_API uint64_t xds_source_request(xds *s, uint64_t *blksize) {
  *blksize = s->source.blksize;
  return (uint64_t)s->source.getblkno;
}

/* `data` must stay valid until the next XDS_NEED_SOURCE. */
XDS_API int xds_source_block(xds *s, uint64_t blkno, const uint8_t *data, uint64_t len) {
  if (!s->pull || len > s->source.blksize) return XD3_INVALID;
  s->source.curblkno = (xoff_t)blkno;
  s->source.curblk = data;
  s->source.onblk = (usize_t)len;
  return 0;
}

XDS_API void xds_cancel(xds *s) { s->cancel = 1; }

XDS_API uint64_t xds_source_indexed(xds *s) { return (uint64_t)s->stream.srcwin_cksum_pos; }

XDS_API const char *xds_message(xds *s) {
  return s && s->stream.msg ? s->stream.msg : "";
}

XDS_API const char *xds_strerror(int code) {
  const char *m = xd3_strerror(code);
  return m ? m : "system error";
}

XDS_API void xds_free(xds *s) {
  if (!s) return;
  xd3_free_stream(&s->stream);
  free(s);
}
