using Lisere.Video.Probing;

namespace Lisere.Video.Tests;

public class FfprobeParserTests
{
    private const string Sample = """
    {
      "streams": [
        {
          "codec_type": "video",
          "codec_name": "h264",
          "width": 1920,
          "height": 1080,
          "pix_fmt": "yuv420p",
          "avg_frame_rate": "30000/1001",
          "duration": "12.512000",
          "nb_frames": "375",
          "bit_rate": "8000000"
        },
        {
          "codec_type": "audio",
          "codec_name": "aac"
        }
      ],
      "format": {
        "duration": "12.512000",
        "bit_rate": "8200000"
      }
    }
    """;

    [Fact]
    public void ParsesVideoAndAudioStreams()
    {
        var info = FfprobeParser.Parse(Sample, "clip.mp4");

        Assert.Equal(1920, info.Width);
        Assert.Equal(1080, info.Height);
        Assert.Equal(29.97, info.FrameRate, 2);
        Assert.Equal(12.512, info.DurationSeconds, 3);
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal("yuv420p", info.PixelFormat);
        Assert.Equal(375, info.FrameCount);
        Assert.True(info.HasAudio);
        Assert.Equal("aac", info.AudioCodec);
        Assert.True(info.IsValid);
    }

    [Fact]
    public void FallsBackToTheFormatDurationWithoutFrames()
    {
        var json = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "vp9",
              "width": 640,
              "height": 360,
              "pix_fmt": "yuv420p",
              "avg_frame_rate": "0/0",
              "r_frame_rate": "24/1"
            }
          ],
          "format": { "duration": "3.5", "bit_rate": "500000" }
        }
        """;

        var info = FfprobeParser.Parse(json, "clip.webm");

        Assert.Equal(24, info.FrameRate, 3);
        Assert.Equal(3.5, info.DurationSeconds, 3);
        Assert.False(info.HasAudio);
        Assert.Equal(84, info.EstimatedFrameCount);
    }

    [Fact]
    public void RefusesAFileWithoutVideo()
    {
        var json = """
        {
          "streams": [ { "codec_type": "audio", "codec_name": "aac" } ],
          "format": { "duration": "3" }
        }
        """;

        Assert.Throws<VideoProbeException>(() => FfprobeParser.Parse(json, "audio.m4a"));
    }

    [Fact]
    public void RefusesAStillImageOpenedAsAVideo()
    {
        // ffprobe reports a JPEG as one 25 fps mjpeg stream in an image2
        // container: without this guard the video page opens it as a 0.04 s
        // clip and renders a broken one-frame export.
        var json = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "mjpeg",
              "width": 640,
              "height": 360,
              "pix_fmt": "yuvj420p",
              "avg_frame_rate": "25/1",
              "r_frame_rate": "25/1",
              "duration": "0.040000"
            }
          ],
          "format": {
            "duration": "0.040000",
            "format_name": "image2"
          }
        }
        """;

        var exception = Assert.Throws<VideoStillImageException>(
            () => FfprobeParser.Parse(json, "photo.jpg"));
        Assert.Contains("image fixe", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesACoverArtAttachedToAnAudioFile()
    {
        var json = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "mjpeg",
              "width": 500,
              "height": 500,
              "avg_frame_rate": "90000/1",
              "disposition": { "attached_pic": 1 }
            },
            { "codec_type": "audio", "codec_name": "mp3" }
          ],
          "format": { "duration": "180", "format_name": "mp3" }
        }
        """;

        Assert.Throws<VideoStillImageException>(
            () => FfprobeParser.Parse(json, "track.mp3"));
    }

    [Fact]
    public void ARealContainerIsNotMistakenForAStillImage()
    {
        var json = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "h264",
              "width": 640,
              "height": 360,
              "avg_frame_rate": "25/1"
            }
          ],
          "format": { "duration": "4", "format_name": "mov,mp4,m4a,3gp,3g2,mj2" }
        }
        """;

        var info = FfprobeParser.Parse(json, "clip.mp4");
        Assert.Equal(640, info.Width);
    }
}
