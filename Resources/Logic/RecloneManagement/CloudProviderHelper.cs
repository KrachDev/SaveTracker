using System;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public class CloudProviderHelper
    {
        public string GetProviderType(CloudProvider provider)
        {
            return provider switch
            {
                CloudProvider.GoogleDrive => "drive",
                CloudProvider.OneDrive => "onedrive",
                CloudProvider.Dropbox => "dropbox",
                CloudProvider.Pcloud => "pcloud",
                CloudProvider.Box => "box",
                CloudProvider.AmazonDrive => "amazonclouddrive",
                CloudProvider.Yandex => "yandex",
                CloudProvider.PutIo => "putio",
                CloudProvider.HiDrive => "hidrive",
                CloudProvider.Uptobox => "uptobox",
                _ => throw new ArgumentException($"Unsupported provider: {provider}")
            };
        }

        public string GetProviderConfigName(CloudProvider provider)
        {
            return provider switch
            {
                CloudProvider.GoogleDrive => "gdrive",
                CloudProvider.OneDrive => "onedrive",
                CloudProvider.Dropbox => "dropbox",
                CloudProvider.Pcloud => "pcloud",
                CloudProvider.Box => "box",
                CloudProvider.AmazonDrive => "amazondrive",
                CloudProvider.Yandex => "yandex",
                CloudProvider.PutIo => "putio",
                CloudProvider.HiDrive => "hidrive",
                CloudProvider.Uptobox => "uptobox",
                _ => throw new ArgumentException($"Unsupported provider: {provider}")
            };
        }

        public string GetProviderConfigType(CloudProvider provider)
        {
            return provider switch
            {
                CloudProvider.GoogleDrive => "drive",
                CloudProvider.OneDrive => "onedrive",
                CloudProvider.Dropbox => "dropbox",
                CloudProvider.Pcloud => "pcloud",
                CloudProvider.Box => "box",
                CloudProvider.AmazonDrive => "amazonclouddrive",
                CloudProvider.Yandex => "yandex",
                CloudProvider.PutIo => "putio",
                CloudProvider.HiDrive => "hidrive",
                CloudProvider.Uptobox => "uptobox",
                _ => throw new ArgumentException($"Unsupported provider: {provider}")
            };
        }

        public bool RequiresTokenValidation(CloudProvider provider)
        {
            return provider switch
            {
                CloudProvider.GoogleDrive => true,
                CloudProvider.OneDrive => true,
                CloudProvider.Dropbox => true,
                CloudProvider.Pcloud => true,
                CloudProvider.Box => true,
                CloudProvider.AmazonDrive => true,
                CloudProvider.Yandex => true,
                CloudProvider.PutIo => true,
                CloudProvider.HiDrive => true,
                CloudProvider.Uptobox => true,
                _ => false
            };
        }
        // Method to check if provider uses username/password
        public string GetProviderDisplayName(CloudProvider provider)
        {
            return provider switch
            {
                CloudProvider.GoogleDrive => "Google Drive",
                CloudProvider.OneDrive => "OneDrive",
                CloudProvider.Dropbox => "Dropbox",
                CloudProvider.Pcloud => "pCloud",
                CloudProvider.Box => "Box",
                CloudProvider.AmazonDrive => "Amazon Drive",
                CloudProvider.Yandex => "Yandex Disk",
                CloudProvider.PutIo => "Put.io",
                CloudProvider.HiDrive => "HiDrive",
                CloudProvider.Uptobox => "Uptobox",
                _ => throw new ArgumentException($"Unsupported provider: {provider}")
            };
        }
    }
}
public enum CloudProvider
{
    GoogleDrive = 0,
    OneDrive = 1,
    Dropbox = 2,
    Pcloud = 3,
    Box = 4,
    AmazonDrive = 5,
    Yandex = 6,
    PutIo = 7,
    HiDrive = 8,
    Uptobox = 9,
    Global = 999
}
