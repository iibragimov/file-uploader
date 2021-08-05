using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YandexDisk.Client;
using YandexDisk.Client.Http;
using YandexDisk.Client.Protocol;

namespace file_uploader
{
    public class App
    {
        private readonly ILogger<App> _logger;
        private readonly IDiskApi _diskApi;

        public App(ILogger<App> logger, IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _diskApi = new DiskHttpApi(appSettings.Value.OAuthToken);
        }

        public async Task Run(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("First argument is the path to directory in your computer");
                Console.WriteLine("Second argument is the path to directory in yandex disk");
                return;
            }

            var localDirectoryPath = args[0];
            var destinationDirectoryPath = args[1];
            if (!Directory.Exists(localDirectoryPath))
            {
                Console.WriteLine($"There is no directory: {localDirectoryPath}");
                return;
            }

            var localFiles = Directory.GetFiles(localDirectoryPath);
            if (!localFiles.Any())
            {
                Console.WriteLine($"There is no files in directory: {localDirectoryPath}");
                return;
            }

            try
            {
                var destinationPath = await GetDestinationPath(destinationDirectoryPath);
                var uploadingTasks = localFiles
                    .Select(localFile =>
                        UploadFile(localFile, destinationPath)
                            .ContinueWith(x => { Console.WriteLine($"{x.Result} is uploaded"); }));
                await Task.WhenAll(uploadingTasks);
            }
            catch (NotAuthorizedException e)
            {
                _logger.LogError(e, "Invalid OAuthToken");
                Console.WriteLine("Invalid OAuthToken");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unhandled exception");
                Console.WriteLine("Unhandled exception");
            }
        }

        private async Task<string> UploadFile(string localFile, string destinationPath)
        {
            var fileName = Path.GetFileName(localFile);
            Console.WriteLine($"{fileName} is being uploaded...");
            var uploadLink = await _diskApi.Files.GetUploadLinkAsync($"{destinationPath}{fileName}", true);
            await using var fs = File.OpenRead(localFile);
            await _diskApi.Files.UploadAsync(uploadLink, fs);
            return fileName;
        }

        private async Task<string> GetDestinationPath(string destinationDirectoryPath)
        {
            var directories = destinationDirectoryPath
                .Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Split(Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var currentPath = Path.AltDirectorySeparatorChar.ToString();
            var rootDirectory = await _diskApi.MetaInfo.GetInfoAsync(new ResourceRequest
            {
                Path = currentPath
            });
            var i = 0;
            // find not existing directory
            for (; i < directories.Length; i++)
            {
                if (rootDirectory.Embedded.Items.All(x => x.Name != directories[i]))
                {
                    break;
                }

                currentPath += $"{directories[i]}{Path.AltDirectorySeparatorChar}";
                rootDirectory = await _diskApi.MetaInfo.GetInfoAsync(new ResourceRequest
                {
                    Path = currentPath
                });
            }

            // create not existing directory
            for (; i < directories.Length; i++)
            {
                currentPath += $"{directories[i]}{Path.AltDirectorySeparatorChar}";
                await _diskApi.Commands.CreateDictionaryAsync(currentPath);
            }

            return currentPath;
        }
    }
}
