namespace file_storage
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            FileStorageCore fileStorageCore = new FileStorageCore("storage", "127.0.0.3", "12346");
        }
    }
}
