namespace file_storage
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            FileStorageCore fileStorageCore = new FileStorageCore("FileStorage", "127.0.0.2", "12345");
        }
    }
}
