﻿#if NETCOREAPP3_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using QuestPDF.Drawing;
using QuestPDF.Drawing.Exceptions;
using QuestPDF.Previewer.Inspection;

namespace QuestPDF.Previewer
{
    internal sealed class NotifyPresenceRequest
    {
        public string Id { get; set; }
        public bool IsDotnet6OrBeyond { get; set; }
        public bool IsDotnet3OrBeyond { get; set; }
        public bool IsExecutedInUnitTest { get; set; }
    }
    
    internal sealed class GenericError
    {
        public string ExceptionType { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public GenericError? InnerException { get; set; }
    }

    internal sealed class ShowGenericErrorApiRequest
    {
        public GenericError? Error { get; set; }
    }
    
    internal sealed class ShowLayoutErrorApiRequest
    {
        public LayoutRenderingTrace? Trace { get; set; }
    }


    internal sealed class UpdateDocumentPreviewApiRequest
    {
        public ICollection<PageSnapshot> PageSnapshots { get; set; }
        public InspectionElement DocumentHierarchy { get; set; }
        
        public class PageSnapshot
        {
            public string ResourceId { get; } = Guid.NewGuid().ToString("N");
            public int PageNumber { get; set; }
            
            public float Width { get; set; }
            public float Height { get; set; }
        }
    }
    
    

    internal class PreviewerService
    {
        private string ClientId { get; } = Guid.NewGuid().ToString("D");
        
        private int Port { get; }
        private HttpClient HttpClient { get; }
        
        public  event Action? OnPreviewerStopped;

        private const int RequiredPreviewerVersionMajor = 2022;
        private const int RequiredPreviewerVersionMinor = 11;
        
        public PreviewerService(int port)
        {
            Port = port;
            HttpClient = new()
            {
                BaseAddress = new Uri($"http://localhost:{port}/"), 
                Timeout = TimeSpan.FromSeconds(1)
            };
        }

        private void StartPreviewer()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new()
                    {
                        FileName = "questpdf-previewer",
                        Arguments = $"{Port}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
            }
            catch
            {
                throw new Exception("Cannot start the QuestPDF Previewer tool. " +
                                    "Please install it by executing in terminal the following command: 'dotnet tool install --global QuestPDF.Previewer'.");
            }
        }
        
        public async Task Connect()
        {
            var isAvailable = await IsPreviewerAvailable();
        
            if (!isAvailable)
            {
                //StartPreviewer();
                //await WaitForConnection();
            }
            
            //var previewerVersion = await GetPreviewerVersion();
            //CheckVersionCompatibility(previewerVersion);

            StartNotifyPresenceTask();
        }

        private async Task<bool> IsPreviewerAvailable()
        {
            try
            {
                using var result = await HttpClient.GetAsync("/ping");
                return result.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public void StartNotifyPresenceTask()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    NotifyPresence();
                    await Task.Delay(1000);
                }
            });
        }
        
        public async Task NotifyPresence()
        {
            var payload = new NotifyPresenceRequest
            {
                Id = ClientId,
                IsDotnet6OrBeyond = RuntimeDetector.IsNet6OrGreater,
                IsDotnet3OrBeyond = RuntimeDetector.IsNet3OrGreater,
                IsExecutedInUnitTest = UnitTestDetector.RunningInUnitTest
            };
 
            await HttpClient.PostAsJsonAsync("/v1/notify/presence", payload);
        }
        
        
        
        public async Task ShowDocumentPreview(DocumentPreviewResult documentPreviewResult)
        {
            using var multipartContent = new MultipartFormDataContent();
            
            var pages = new List<UpdateDocumentPreviewApiRequest.PageSnapshot>();
            
            foreach (var snapshot in documentPreviewResult.PageSnapshots)
            {
                var page = new UpdateDocumentPreviewApiRequest.PageSnapshot
                {
                    PageNumber = documentPreviewResult.PageSnapshots.IndexOf(snapshot) + 1,
                    Width = snapshot.Size.Width,
                    Height = snapshot.Size.Height
                };
                
                pages.Add(page);
            
                var pictureStream = snapshot.Picture.Serialize().AsStream();
                multipartContent.Add(new StreamContent(pictureStream), "snapshots", page.ResourceId);
            }
            
            var request = new UpdateDocumentPreviewApiRequest
            {
                PageSnapshots = pages,
                DocumentHierarchy = documentPreviewResult.DocumentHierarchy
            };
            
            var json = JsonSerializer.Serialize(request);
            var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");
            multipartContent.Add(jsonContent, "request");
            
            using var response = await HttpClient.PostAsync("/v1/update/preview/document", multipartContent);
            response.EnsureSuccessStatusCode();
            
            foreach (var picture in documentPreviewResult.PageSnapshots)
                picture.Picture.Dispose();
        }
        
        public async Task ShowGenericError(Exception exception)
        {
            var payload = new ShowGenericErrorApiRequest
            {
                Error = MapException(exception)
            };

            await HttpClient.PostAsJsonAsync("/v1/update/error/generic", payload);
            
            static GenericError? MapException(Exception? exception)
            {
                if (exception == null)
                    return null;
                        
                return new GenericError
                {
                    ExceptionType = exception.GetType().FullName, 
                    Message = exception.Message, 
                    StackTrace = exception.StackTrace, 
                    InnerException = MapException(exception.InnerException)
                };
            }
        }
        
        public async Task ShowLayoutError(DocumentLayoutException exception)
        {
            var payload = new ShowLayoutErrorApiRequest
            {
                Trace = exception.ElementTrace
            };

            await HttpClient.PostAsJsonAsync("/v1/update/error/layout", payload);
        }
        

        

        //
        // private async Task<Version> GetPreviewerVersion()
        // {
        //     using var result = await HttpClient.GetAsync("/version");
        //     return await result.Content.ReadFromJsonAsync<Version>();
        // }
        //

        

        
        
        //
        // private void CheckVersionCompatibility(Version version)
        // {
        //     if (version.Major == RequiredPreviewerVersionMajor && version.Minor == RequiredPreviewerVersionMinor)
        //         return;
        //     
        //     throw new Exception($"Previewer version is not compatible. Possible solutions: " +
        //                         $"1) Update the QuestPDF library to newer version. " +
        //                         $"2) Update the QuestPDF previewer tool using the following command: 'dotnet tool update --global QuestPDF.Previewer --version {RequiredPreviewerVersionMajor}.{RequiredPreviewerVersionMinor}'");
        // }
        //
        // private async Task WaitForConnection()
        // {
        //     using var cancellationTokenSource = new CancellationTokenSource();
        //     cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));
        //     
        //     var cancellationToken = cancellationTokenSource.Token; 
        //     
        //     while (true)
        //     {
        //         await Task.Delay(TimeSpan.FromMilliseconds(250));
        //
        //         if (cancellationToken.IsCancellationRequested)
        //             throw new Exception($"Cannot connect to the QuestPDF Previewer tool. Please make sure that your Operating System does not block HTTP connections on port {Port}.");
        //
        //         var isConnected = await IsPreviewerAvailable();
        //
        //         if (isConnected)
        //             break;
        //     }
        // }
    }
}

#endif
