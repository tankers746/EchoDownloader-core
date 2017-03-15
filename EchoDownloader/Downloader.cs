using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace echoDownloader
{
    class Downloader
    {
        private ILogger logger { get; } = Cli.LoggerFactory.CreateLogger<Downloader>();
        Config c;
        bool verbose;
        int queueSize;

        public Downloader(Config c, bool verbose)
        {
            this.c = c;
            this.verbose = verbose;
        }

        public async Task Download()
        {
            var queue = c.FilterEchoes(false).Select(e => DownloadEcho(e)).ToArray();
            queueSize = queue.Length;
            if(queueSize > 0 && CheckDownloadsFolder() && CheckFFmpeg())
            {
                logger.LogInformation("Downloading {queueSize} lecture(s).", queueSize);
                await Task.WhenAll(queue);
                logger.LogInformation("Finished downloading lectures.");
            }
        }

        async Task DownloadEcho(Echo e)
        {
            var basePath = Path.Combine(c.downloads, e.unit);
            var fileName = string.Format("S01E{0:00} - {1}", e.episode, e.title);
            var ext = ".mp4";
            var n = 1;
            var modifier = "";
            while(File.Exists(Path.Combine(basePath, $"{ fileName }{ modifier }{ ext }")))
            {
                modifier = $" ({ n++ })";
            }
            fileName = $"{ fileName }{ modifier }{ ext }";

            var filePath = Path.Combine(basePath, fileName);
            Directory.CreateDirectory(basePath);
            var args = BuildFFmpegArgs(e, filePath);
            e.downloaded = await RunFFmpeg(args);
            if (e.downloaded)
            {
                c.SaveEchoFile();
                logger.LogInformation("Succesfully downloaded \"{fileName}\"", fileName);
            }
            else {
                if(File.Exists(filePath)) File.Delete(filePath);
                logger.LogError("Failed to download \"{fileName}\"", fileName);
            }
            logger.LogInformation("{queueSize} lecture(s) in the download queue.", Interlocked.Decrement(ref queueSize));
        }

        async Task<bool> RunFFmpeg(string args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = false;
            startInfo.UseShellExecute = false;
            startInfo.FileName = "ffmpeg.exe";
            startInfo.Arguments = args;
            if(!verbose)
            {
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
            }
            logger.LogDebug("Executing {fileName} with arguments {args}", startInfo.FileName, startInfo.Arguments);
            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    process.EnableRaisingEvents = true;
                    if (!verbose)
                    {
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                    }
                    var processExitedSource = new TaskCompletionSource<int>();
                    process.Exited += (o, e) => processExitedSource.SetResult(process.ExitCode);
                    return await processExitedSource.Task == 0; 

                }
            }
            catch (Exception ex)
            {
                logger.LogDebug("Execution failed for \"{fileName}\" with arguments {args}", startInfo.FileName, startInfo.Arguments);
                logger.LogDebug(ex.StackTrace);
                return false;
            }
        }

        string BuildFFmpegArgs(Echo e, string filePath)
        {
            var args = new List<string>();
            args.Add($"-i \"{ e.url }\"");

            if (e.url.EndsWith("mp3"))
            {
                args.Add("-f lavfi");
                args.Add("-i color=s=640x480:r=10");
                args.Add("-c:v libx264");
                args.Add("-c:a aac");
                args.Add("-shortest");
            }
            else args.Add("-c copy");

            if (e.url.EndsWith("m3u8"))
                args.Add("-bsf:a aac_adtstoasc");

            args.Add($"-metadata show=\"{ e.unit } - { e.unitName }\"");
            args.Add($"-metadata title=\"{ e.title } - { e.description }\"");
            args.Add($"-metadata episode_sort={ e.episode }");
            args.Add($"-metadata description=\"{ e.description }\"");

            //media type is tv show for iTunes
            args.Add("-metadata");
            args.Add("media_type=10");

            args.Add($"\"{ filePath }\"");
            return string.Join(" ", args.ToArray());
        }

        public bool CheckFFmpeg()
        {
            if (RunFFmpeg("-?").Result)
            {
                return true;
            }
            else
            {
                logger.LogError("Unable to access ffmpeg.exe, ensure it is in the same folder");
                return false;
            }
        }

        public bool CheckDownloadsFolder()
        {
            if (Directory.Exists(c.downloads))
            {
                return true;
            }
            else
            {
                logger.LogError("Path to downloads folder specified in {configFile} is not valid '{downloads}'", Config.configFile, c.downloads);
                return false;
            }
        }
    }
}
