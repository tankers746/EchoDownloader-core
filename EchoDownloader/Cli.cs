using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace echoDownloader
{
    class Cli
    {
        public static ILoggerFactory LoggerFactory { get; } = new LoggerFactory().AddFile("logs/log.txt", LogLevel.Debug);
        private static ILogger logger { get; } = LoggerFactory.CreateLogger<Cli>();

        static void Main(string[] args)
        {
            var commandLineApplication = new CommandLineApplication(throwOnUnexpectedArg: false);
            CommandOption fetch = commandLineApplication.Option(
                "-f | --fetch", "Fetch new lectures", CommandOptionType.NoValue);
            CommandOption download = commandLineApplication.Option(
                "-d | --download", "Downloads any undownloaded lectures that match the parameters specified in " + Config.configFile, CommandOptionType.NoValue);
            CommandOption setDownloaded = commandLineApplication.Option(
                "-s | --setdownloaded <true/false>", "Sets the downloaded value of lectures matching the filter to the given value", CommandOptionType.SingleValue);
            CommandOption list = commandLineApplication.Option(
                "-l | --list", "Lists the fetched lectures that match the parameters specified in " + Config.configFile, CommandOptionType.NoValue);
            CommandOption verbose = commandLineApplication.Option(
                "-v | --verbose", "Enables verbose logging", CommandOptionType.NoValue);
            CommandOption username = commandLineApplication.Option(
                "-u | --username <username>", "Student number for LMS login", CommandOptionType.SingleValue);
            CommandOption password = commandLineApplication.Option(
                "-p | --password <password>", "Password for LMS login", CommandOptionType.SingleValue);
            commandLineApplication.HelpOption("-? | -h | --help");
            commandLineApplication.OnExecute(() =>
            {
                if (verbose.HasValue())
                {
                    LoggerFactory.AddConsole(LogLevel.Debug);
                }
                else
                {
                    LoggerFactory.AddConsole(LogLevel.Information);
                }
                Config config = LoadConfig();
                if (fetch.HasValue())
                {
                    //Check that we can login to LMS
                    if (username.HasValue() && password.HasValue())
                    {
                        var fetcher = new Fetcher(config, username.Value(), password.Value());
                        fetcher.FetchAsync().Wait();
                    }
                    else logger.LogWarning("Please specify a username and password for logging into LMS");
                }
                if (download.HasValue())
                {
                    Downloader downloader = new Downloader(config, verbose.HasValue());
                    downloader.Download().Wait();
                }
                if (setDownloaded.HasValue())
                {
                    config.setEchoesDownloaded(setDownloaded.Value());
                }
                if(list.HasValue())
                {
                    config.PrintFiltered();
                }
                return 0;
            });
            commandLineApplication.Execute(args);
        }

        static Config LoadConfig()
        {
            Config c;
            try
            {
                var configJson = File.ReadAllText(Config.configFile);
                c = JsonConvert.DeserializeObject<Config>(configJson);
            }
            catch (Exception e)
            {
                logger.LogWarning("Unable to read {0}", Config.configFile);
                c = new Config();
                var configJson = JsonConvert.SerializeObject(c, Formatting.Indented);
                File.WriteAllText(Config.configFile, configJson);
                logger.LogInformation("Written new configuration file to {configFile} please add downloads folder to file.", Config.configFile);
            }
            try
            {
                var echoesJson = File.ReadAllText(c.echoFile);
                c.echoes = JsonConvert.DeserializeObject<ConcurrentDictionary<string, Echo>>(echoesJson);

            }
            catch (Exception e)
            {
                logger.LogWarning("Unable to read {0}", c.echoFile);
                c.echoes = new ConcurrentDictionary<string, Echo>();
            }
            return c;
        }
    }
}
