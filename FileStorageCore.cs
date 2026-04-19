using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace file_storage
{
    public class FileStorageCore
    {
        IPAddress fileStorageIP;
        int port;
        Socket acceptClients;
        string path;

        public FileStorageCore(string path, string iPAddress = "127.0.0.2", string port = "12345")
        {
            path = Path.GetFullPath(path);
            fileStorageIP = IPAddress.Parse(iPAddress);
            this.port = int.Parse(port);
            Console.WriteLine($"Файловое хранилище на IP адресе {fileStorageIP.ToString()} с портом {port} и путем {path} успешно создано");
        }

        public async Task StartWorking()
        {

        }
    }
}
