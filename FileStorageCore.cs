using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace file_storage
{
    public class FileStorageCore
    {
        IPAddress fileStorageIP;
        int port;
        Socket acceptClients;
        string path;
        bool isAlive = false;

        public FileStorageCore(string path = "FileStorage", string iPAddress = "127.0.0.2", string port = "12345")
        {
            this.path = Path.GetFullPath(path);
            Directory.CreateDirectory(path);
            fileStorageIP = IPAddress.Parse(iPAddress);
            this.port = int.Parse(port);
            acceptClients = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Console.WriteLine($"Файловое хранилище на IP адресе {fileStorageIP.ToString()} с портом {port} и путем {path} успешно создано");
        }

        public async Task StartWorking()
        {
            try
            {
                acceptClients.Bind(new IPEndPoint(fileStorageIP, port));
                acceptClients.Listen(10);
                isAlive = true;
                
                while (isAlive)
                {
                    Socket client = await acceptClients.AcceptAsync();
                    try
                    {
                        await ReadHTTPFromClient(client);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Произошла ошибка: {e.Message}");
                    }
                    finally
                    {
                        client.Shutdown(SocketShutdown.Send);
                        client.Close();
                    }
                    
                }
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"Произошла ошибка: {ex.Message}");
            }
            finally
            {
                isAlive = false;
                acceptClients?.Close();
            }
        }

        public async Task ReadHTTPFromClient(Socket client)
        {
            byte[] buffer = new byte[8192];
            List<byte> data = new List<byte>();
            int sizeBody = -1;
            int startBody = -1;
            while (isAlive)
            {
                int len = await client.ReceiveAsync(buffer);
                if (len == 0) break;

                data.AddRange(buffer.Take(len));
                if (IsFullHttp(data.ToArray(), ref startBody, ref sizeBody))
                {
                    byte[] HTTPAnswer = await ParseHTTP(data.ToArray(), startBody, sizeBody);
                    await SendAnswer(HTTPAnswer, client);
                    data.Clear();
                    sizeBody = -1;
                    startBody = -1;
                }
            }
        }

        public bool IsFullHttp(byte[] data, ref int startBody, ref int sizeBody)
        {
            string http = Encoding.ASCII.GetString(data);
            if (startBody == -1)
            {
                startBody = http.IndexOf("\r\n\r\n");
                if (startBody == -1) return false;
                startBody += 4;
            }
            if (sizeBody == -1)
            {
                string[] parts = http.Split("\r\n");
                foreach (string part in parts)
                {
                    if (part.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int pos = part.IndexOf(':');
                        string value = part.Substring(pos + 1).Trim();
                        sizeBody = int.Parse(value);
                    }
                }
                if (sizeBody == -1) sizeBody = 0; // обработка того что заголовка Content-Length просто не было, фактически заглушка
            }

            if (data.Length == sizeBody + startBody) return true;
            return false;
        }

        public async Task<byte[]> ParseHTTP(byte[] data, int startBody, int sizeBody)
        {
            string http = Encoding.ASCII.GetString(data, 0, startBody);
            string[] parts = http.Split("\r\n");
            string[] firstParts = parts[0].Split(' ', 3);
            string method = firstParts[0];
            string localPath = firstParts[1];
            string versionHTTP = firstParts[2];

            int status = 500;
            string statusText = "Internal Server Error";

            string relativePath = Uri.UnescapeDataString(localPath);
            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            relativePath = relativePath.TrimStart(Path.DirectorySeparatorChar);
            string fullPathFile = Path.Combine(this.path, relativePath);

            bool isDirectory = Directory.Exists(fullPathFile);
            bool isFile = File.Exists(fullPathFile);

            if (!fullPathFile.StartsWith(this.path, StringComparison.OrdinalIgnoreCase))
            {
                status = 403;
                statusText = "Forbidden";
                return await BuildHttpResponse(status, statusText, Encoding.UTF8.GetBytes("Защита от доступа к папкам вне файлового хранилища"));
            }

            switch (method)
            {
                case "PUT":
                    if (isDirectory)
                    {
                        return await BuildHttpResponse(400, "Bad Request", Encoding.UTF8.GetBytes("Попытка записать на место существующего каталога файл"));
                    }
                    string? copyFromPath = null;
                    foreach (string part in parts)
                    {
                        if (part.StartsWith("X-Copy-From:", StringComparison.OrdinalIgnoreCase))
                        {
                            int pos = part.IndexOf(':');
                            copyFromPath = part.Substring(pos + 1).Trim();
                        }
                    }

                    string? directoryFile = Path.GetDirectoryName(fullPathFile);
                    if (!string.IsNullOrEmpty(directoryFile)) Directory.CreateDirectory(directoryFile); // если путь не существует то создаем папки до этого пути

                    byte[] body;

                    bool existed = File.Exists(fullPathFile);
                    status = existed ? 200 : 201;
                    statusText = existed ? "OK" : "Created";

                    if (copyFromPath == null)
                    {
                        body = new byte[sizeBody];
                        Array.Copy(data, startBody, body, 0, sizeBody);
                        await File.WriteAllBytesAsync(fullPathFile, body);
                        return await BuildHttpResponse(status, statusText, Encoding.UTF8.GetBytes("Операция успешно проведена"));
                    }
                    else
                    {
                        string relativePathCopy = Uri.UnescapeDataString(copyFromPath);
                        relativePathCopy = relativePathCopy.Replace('/', Path.DirectorySeparatorChar);
                        relativePathCopy = relativePathCopy.TrimStart(Path.DirectorySeparatorChar);
                        string fullPathFileCopy = Path.Combine(this.path, relativePathCopy);
                        if (!File.Exists(fullPathFileCopy))
                        {
                            status = 404;
                            statusText = "Not Found";
                            return await BuildHttpResponse(status, statusText, Encoding.UTF8.GetBytes("Копируемый файл отсутствует"));
                        }
                        else if (fullPathFileCopy == fullPathFile)
                        {
                            status = 400;
                            statusText = "Bad Request";
                            return await BuildHttpResponse(status, statusText, Encoding.UTF8.GetBytes("Пути файлов для копирования совпадают"));
                        }
                        else
                        {
                            File.Copy(fullPathFileCopy, fullPathFile);
                            return await BuildHttpResponse(status, statusText, Encoding.UTF8.GetBytes("Операция по копированию успешно проведена"));
                        }
                    }
                case "GET":
                    if (isFile)
                    {
                        byte[] fileBytes = await File.ReadAllBytesAsync(fullPathFile);
                        var fileInfo = new FileInfo(fullPathFile);
                        string contentType = GetContentType(fullPathFile); 
                        return await BuildHttpResponse(200, "OK", fileBytes, contentType, fileInfo);
                    }
                    else if (isDirectory)
                    {
                        var dirInfo = new DirectoryInfo(fullPathFile);
                        var items = dirInfo.GetFileSystemInfos().Select(fsi => new
                            {
                                name = fsi.Name,
                                type = fsi is DirectoryInfo ? "directory" : "file",
                                size = fsi is FileInfo fi ? fi.Length : (long?)null,
                                lastModified = fsi.LastWriteTimeUtc.ToString("R")
                            });
                        string json = System.Text.Json.JsonSerializer.Serialize(items);
                        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                        return await BuildHttpResponse(200, "OK", jsonBytes, "application/json");
                    }
                    else
                    {
                        return await BuildHttpResponse(404, "Not Found", Encoding.UTF8.GetBytes("Файл или каталог не найден"));
                    }
                case "HEAD":
                    if (isFile)
                    {
                        var fileInfo = new FileInfo(fullPathFile);
                        string contentType = GetContentType(fullPathFile);
                        return await BuildHttpResponse(200, "OK", null, contentType, fileInfo);
                    }
                    else if (isDirectory) // заглушка
                    {
                        return await BuildHttpResponse(200, "OK", null, "application/json");
                    }
                    else
                    {
                        return await BuildHttpResponse(404, "Not Found", Encoding.UTF8.GetBytes("Файл или каталог не найден"));
                    }
                case "DELETE":
                    if (isFile)
                    {
                        File.Delete(fullPathFile);
                        return await BuildHttpResponse(200, "OK", Encoding.UTF8.GetBytes("Файл удалён"));
                    }
                    else if (isDirectory)
                    {
                        Directory.Delete(fullPathFile, true);
                        return await BuildHttpResponse(200, "OK", Encoding.UTF8.GetBytes("Каталог удалён"));
                    }
                    else
                    {
                        return await BuildHttpResponse(404, "Not Found");
                    }
                default:
                    Console.WriteLine($"Получен неизвестный метод: {method}");
                    return await BuildHttpResponse(status, statusText, Encoding.UTF8.GetBytes($"Получен неизвестный метод: {method}"));
            }
        }

        // fileinfo указывается когда надо получить данные по последнему изменению файлов (не каталогов!) (get, head)
        public async Task<byte[]> BuildHttpResponse(int status, string statusText, byte[]? body = null, string contentType = "text/plain", FileInfo fileInfo = null) 
        {
            if (body == null) body = new byte[0];
            string headers = $"HTTP/1.1 {status} {statusText}\r\nContent-Type: {contentType}\r\n";
            if (fileInfo != null)
            {
                headers += $"Content-Length: {fileInfo.Length}\r\nLast-Modified: {fileInfo.LastWriteTimeUtc.ToString("R")}\r\n\r\n";
            }
            else
            {
                headers += $"Content-Length: {body.Length}\r\n\r\n";
            }

            byte[] headersBytes = Encoding.ASCII.GetBytes(headers);
            byte[] fullHTTP = new byte[headersBytes.Length + body.Length];
            Array.Copy(headersBytes, 0, fullHTTP, 0, headersBytes.Length);
            Array.Copy(body, 0, fullHTTP, headersBytes.Length, body.Length);
            return fullHTTP;
        }

        private string GetContentType(string filePath)
        {
            string ext = Path.GetExtension(filePath)?.ToLowerInvariant() ?? "";

            switch (ext)
            {
                case ".txt":
                case ".log":
                    return "text/plain";
                case ".html":
                case ".htm":
                    return "text/html";
                case ".css":
                    return "text/css";
                case ".js":
                    return "application/javascript";
                case ".json":
                    return "application/json";
                case ".xml":
                    return "application/xml";
                case ".pdf":
                    return "application/pdf";
                case ".zip":
                    return "application/zip";
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".gif":
                    return "image/gif";
                case ".svg":
                    return "image/svg+xml";
                case ".ico":
                    return "image/x-icon";
                case ".mp3":
                    return "audio/mpeg";
                case ".mp4":
                    return "video/mp4";
                case ".webm":
                    return "video/webm";
                default:
                    return "application/octet-stream";
            }
        }

        public async Task SendAnswer(byte[] answer, Socket socket)
        {
            await socket.SendAsync(answer);
        }
    }
}
