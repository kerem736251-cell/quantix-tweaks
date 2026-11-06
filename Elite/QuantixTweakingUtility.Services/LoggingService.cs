using System;
using System.IO;

namespace QuantixTweakingUtility.Services;

public sealed class LoggingService : ILoggingService
{
	private readonly object _lock = new object();

	private readonly string _logDirectory;

	public string CurrentLogFilePath { get; }

	public LoggingService()
	{
		_logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantix", "Logs");
		Directory.CreateDirectory(_logDirectory);
		CurrentLogFilePath = Path.Combine(_logDirectory, $"quantix-{DateTime.Now:yyyyMMdd}.log");
	}

	public void Info(string source, string message)
	{
		Write("INFO", source, message);
	}

	public void Warn(string source, string message)
	{
		Write("WARN", source, message);
	}

	public void Error(string source, string message, Exception? ex = null)
	{
		string message2 = ((ex == null) ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");
		Write("ERROR", source, message2);
	}

	private void Write(string level, string source, string message)
	{
		string text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {source}: {message}";
		lock (_lock)
		{
			try
			{
				File.AppendAllText(CurrentLogFilePath, text + Environment.NewLine);
			}
			catch
			{
			}
		}
	}
}
