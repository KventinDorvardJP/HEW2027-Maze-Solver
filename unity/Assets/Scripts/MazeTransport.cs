using System;
using System.Collections;
using System.Text;
using UnityEngine.Networking;

namespace MazeSolver
{
    public sealed class HttpReply
    {
        public bool Success;
        public string Body;
        public string Error;
    }

    public interface IMazeTransport
    {
        IEnumerator Send(string path, string json, int timeout, Action<HttpReply> complete);
        void Cancel();
    }

    public sealed class MazeTransport : IMazeTransport
    {
        private readonly string baseUrl;
        private UnityWebRequest active;

        public MazeTransport(string baseUrl) { this.baseUrl = baseUrl.TrimEnd('/'); }

        public IEnumerator Send(string path, string json, int timeout, Action<HttpReply> complete)
        {
            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation = null;
            string startError = null;
            try
            {
                request = new UnityWebRequest(baseUrl + path, json == null ? "GET" : "POST");
                request.downloadHandler = new DownloadHandlerBuffer();
                if (json != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.timeout = timeout;
                operation = request.SendWebRequest();
            }
            catch (Exception exc) { startError = "Could not start HTTP request: " + exc.Message; }
            if (startError != null)
            {
                request?.Dispose();
                complete(new HttpReply { Success = false, Body = "", Error = startError });
                yield break;
            }
            using (request)
            {
                active = request;
                try
                {
                    yield return operation;
                    complete(new HttpReply {
                        Success = request.result == UnityWebRequest.Result.Success,
                        Body = request.downloadHandler.text,
                        Error = "HTTP " + request.responseCode + ": " + request.error
                    });
                }
                finally { if (active == request) active = null; }
            }
        }

        public void Cancel()
        {
            // The old coroutine can finish after Abort. The controller's run
            // generation check prevents it from touching the new session.
            active?.Abort();
            active = null;
        }
    }
}
