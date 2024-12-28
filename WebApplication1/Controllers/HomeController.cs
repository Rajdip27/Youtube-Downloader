using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using WebApplication1.Models;
using YoutubeExplode;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpPost]
        public async Task<IActionResult> Download(string videoUrl)
        {
            if (string.IsNullOrWhiteSpace(videoUrl))
            {
                ViewBag.Message = "Invalid URL";
                return View("Index");
            }

            try
            {
                var youtube = new YoutubeClient();
                var videoId = YoutubeExplode.Videos.VideoId.TryParse(videoUrl);

                if (!videoId.HasValue)
                {
                    ViewBag.Message = "Invalid YouTube URL";
                    return View("Index");
                }

                // Fetch video and streams
                var video = await youtube.Videos.GetAsync(videoId.Value);
                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoId.Value);

                // Sanitize the video title to create a valid file name
                var sanitizedTitle = string.Concat(video.Title.Split(Path.GetInvalidFileNameChars()));

                // Use LINQ to find the best muxed stream (audio + video)
                var muxedStreamInfo = streamManifest
                    .GetMuxedStreams()
                    .OrderByDescending(s => s.VideoQuality.MaxHeight)
                    .FirstOrDefault();

                if (muxedStreamInfo != null)
                {
                    // File name for the download
                    var fileName = $"{sanitizedTitle}.mp4";

                    // Download the muxed stream
                    using (var stream = await youtube.Videos.Streams.GetAsync(muxedStreamInfo))
                    {
                        // Save the file to the server
                        var filePath = Path.Combine("wwwroot", "videos", fileName);
                        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                        {
                            await stream.CopyToAsync(fileStream);
                        }

                        ViewBag.Message = "Video downloaded successfully.";
                        return View("Index");
                    }
                }
                else
                {
                    // Fallback to separate audio and video streams
                    var videoStreamInfo = streamManifest
                        .GetVideoOnlyStreams()
                        .OrderByDescending(s => s.VideoQuality.MaxHeight)
                        .FirstOrDefault();

                    var audioStreamInfo = streamManifest
                        .GetAudioOnlyStreams()
                        .OrderByDescending(s => s.Bitrate)
                        .FirstOrDefault();

                    if (videoStreamInfo == null || audioStreamInfo == null)
                    {
                        ViewBag.Message = "Video or audio stream not available for this video.";
                        return View("Index");
                    }

                    // File names for the download
                    var videoFileName = $"{sanitizedTitle}_video.mp4";
                    var audioFileName = $"{sanitizedTitle}_audio.mp4";

                    // Download the video stream
                    var videoFilePath = Path.Combine("wwwroot", "videos", videoFileName);
                    using (var videoStream = await youtube.Videos.Streams.GetAsync(videoStreamInfo))
                    {
                        using (var fileStream = new FileStream(videoFilePath, FileMode.Create, FileAccess.Write))
                        {
                            await videoStream.CopyToAsync(fileStream);
                        }
                    }

                    // Download the audio stream
                    var audioFilePath = Path.Combine("wwwroot", "videos", audioFileName);
                    using (var audioStream = await youtube.Videos.Streams.GetAsync(audioStreamInfo))
                    {
                        using (var fileStream = new FileStream(audioFilePath, FileMode.Create, FileAccess.Write))
                        {
                            await audioStream.CopyToAsync(fileStream);
                        }
                    }

                    // Merge video and audio using FFmpeg
                    var outputFilePath = Path.Combine("wwwroot", "videos", $"{sanitizedTitle}.mp4");
                    var ffmpegCommand = $"ffmpeg -i \"{videoFilePath}\" -i \"{audioFilePath}\" -c:v copy -c:a aac \"{outputFilePath}\"";
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/C {ffmpegCommand}",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit();

                    // Clean up temporary files
                    System.IO.File.Delete(videoFilePath);
                    System.IO.File.Delete(audioFilePath);

                    ViewBag.Message = "Video downloaded and merged successfully.";
                    return View("Index");
                }
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"Error: {ex.Message}";
                return View("Index");
            }
        }


    }
}
