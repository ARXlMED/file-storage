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
                    byte[] HTTPAnswer = await ParseHTTP(data.ToArray(), sizeBody, startBody);
                    await SendAnswer(HTTPAnswer);
                    data.Clear();
                }
            }
        }

        public bool IsFullHttp(byte[] data, ref int startBody, ref int sizeBody)
        {
            string http = Encoding.ASCII.GetString(data);
            if (startBody == -1)
            {
                startBody = http.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (startBody == -1) return false;
                startBody += 4;
            }
            if (sizeBody == -1)
            {
                string[] parts = http.Split("\r\n");
                foreach (string part in parts)
                {
                    if (part.StartsWith("Content-Length:"))
                    {
                        int pos = part.IndexOf(':');
                        string value = part.Substring(pos + 1).Trim();
                        sizeBody = int.Parse(value);
                    }
                }
                if (sizeBody == -1) sizeBody = 0; // обработка того что заголовка Content-Length просто не было, фактически заглушка, нужно смотреть chanked ещё
            }

            if (data.Length == sizeBody + startBody) return true;
            return false;
        }

        public async Task<byte[]> ParseHTTP(byte[] data, int sizeBody, int startBody)
        {

            return new byte[0];
        }

        public async Task SendAnswer(byte[] answer)
        {

        }
    }
}
