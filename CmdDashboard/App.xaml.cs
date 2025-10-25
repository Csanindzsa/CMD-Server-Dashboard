using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using System.Security.Principal;

namespace CmdDashboard;

public partial class App : System.Windows.Application
{
	public static bool IsRunningAsAdministrator { get; private set; }

	static App()
	{
		try
		{
			using var identity = WindowsIdentity.GetCurrent();
			var principal = new WindowsPrincipal(identity);
			IsRunningAsAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			IsRunningAsAdministrator = false;
		}
	}

	protected override void OnStartup(System.Windows.StartupEventArgs e)
	{
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
		base.OnStartup(e);
	}

	private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		var message = $"[DispatcherUnhandledException] {e.Exception.GetType().FullName}: {e.Exception.Message}{Environment.NewLine}{e.Exception.StackTrace}";
		Log(message);
		Console.WriteLine(message);
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is Exception ex)
		{
			var message = $"[UnhandledException] {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
			Log(message);
			Console.WriteLine(message);
		}
	}

	private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
	{
		var message = $"[FirstChance] {e.Exception.GetType().FullName}: {e.Exception.Message}";
		Log(message, append: true, includeStack: false);
	}

	private static void Log(string message, bool append = true, bool includeStack = true)
	{
		try
		{
			var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			var logDirectory = Path.Combine(appData, "CmdDashboard");
			Directory.CreateDirectory(logDirectory);
			var logPath = Path.Combine(logDirectory, "crash.log");
			using var writer = new StreamWriter(logPath, append);
			writer.WriteLine($"[{DateTime.Now:O}] {message}");
			if (includeStack)
			{
				writer.WriteLine();
			}
		}
		catch
		{
			// ignore logging failures
		}
	}
}
