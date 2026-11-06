using System;

namespace QuantixTweakingUtility.Services;

public interface ILoggingService
{
	string CurrentLogFilePath { get; }

	void Info(string source, string message);

	void Warn(string source, string message);

	void Error(string source, string message, Exception? ex = null);
}
