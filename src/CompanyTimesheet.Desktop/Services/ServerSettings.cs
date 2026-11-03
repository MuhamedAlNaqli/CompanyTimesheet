using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace CompanyTimesheet.Desktop.Services;

/// <summary>The address of the company server: what the person typed on the sign-in screen, else the setting of IT, else this computer</summary>
public interface IServerSettings
{
    string Address { get; }

    void Save(string address);
}

public sealed class ServerSettings : IServerSettings
{
    /// <summary>A test on one PC: the server of the test package listens here</summary>
    public const string DefaultAddress = "http://localhost:5090";

    private readonly IConfiguration _configuration;

    public ServerSettings(IConfiguration configuration) => _configuration = configuration;

    private static string FilePath => Path.Combine(Background.ReminderState.FolderPath, "server.txt");

    public string Address
    {
        get
        {
            try
            {
                if (File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() is { Length: > 0 } typed) return typed;
            }
            catch (IOException)
            {
                // An unreadable file is the same as none
            }

            return _configuration["Company:ApiUrl"] is { Length: > 0 } configured ? configured : DefaultAddress;
        }
    }

    public void Save(string address)
    {
        Directory.CreateDirectory(Background.ReminderState.FolderPath);
        File.WriteAllText(FilePath, address.Trim());
    }
}
