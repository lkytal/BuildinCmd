﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Wpf
{
	/// <summary>
	/// MainWindow.xaml 的交互逻辑
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();

			Rst.BorderThickness = new Thickness(0, 0, 0, 0);

			Closing += (s, e) =>
			{
				Proc.EnableRaisingEvents = false;
				Dispose(true);
			};
		}

		~MainWindow()
		{
			Dispose(false);
		}

		private bool MDisposed;

		[SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "ErrorTask"), SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "OutputTask")]
		protected virtual void Dispose(bool disposing)
		{
			if (!MDisposed)
			{
				if (disposing)
				{
					CancelToken.Dispose();
				}
				// free native resources

				MDisposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private Process Proc;
		private Task OutputTask, ErrorTask;
		private int RstLen;
		private CancellationTokenSource CancelToken;

		private void Init()
		{
			CancelToken = new CancellationTokenSource();

			ProcessStartInfo proArgs = new ProcessStartInfo("cmd.exe")
			{
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardInput = true,
				RedirectStandardError = true,
				UseShellExecute = false
			};

			Proc = Process.Start(proArgs);

			if (Proc == null) return;

			Proc.EnableRaisingEvents = true;

			OutputTask = new Task(() => ReadRoutine(Proc.StandardOutput, CancelToken));
			OutputTask.Start();
			ErrorTask = new Task(() => ReadRoutine(Proc.StandardError, CancelToken));
			ErrorTask.Start();

			Proc.Exited += (sender, e) => Restart();
		}

		private void Restart()
		{
			CancelToken.Cancel();
			OutputTask.Wait();
			ErrorTask.Wait();
			CancelToken.Dispose();
			Init();
		}

		private void ExtractDir(ref string outputs)
		{
			string lastLine = outputs.Substring(outputs.LastIndexOf('\n') + 1);

			if (Regex.IsMatch(lastLine, @"^\w:\\\S*>$"))
			{
				dir = lastLine.Substring(0, lastLine.Length - 1);
			}
		}

		private void AddData(string outputs)
		{
			Action act = () =>
			{
				ExtractDir(ref outputs);

				Rst.AppendText(outputs);
				RstLen = Rst.Text.Length;
				Rst.Select(RstLen, 0);
			};

			Dispatcher.BeginInvoke(act);
		}

		private readonly object Locker = new object();
		private bool CmdRepl;
		private void ReadRoutine(StreamReader output, CancellationTokenSource cancelToken)
		{
			char[] data = new char[4096];

			while (!cancelToken.Token.IsCancellationRequested)
			{
				try
				{
					Thread.Sleep(50);

					int len = output.Read(data, 0, 4096);

					StringBuilder str = new StringBuilder();
					str.Append(data, 0, len);

					string outputs = str.ToString();

					if (CmdRepl)
					{
						CmdRepl = false;
						outputs = outputs.Substring(outputs.IndexOf('\n'));
					}

					AddData(outputs);
				}
				catch (IOException)
				{
					return; //Proc terminated
				}
			}
		}

		private readonly List<string> CmdList = new List<string>();
		private int CmdPos = -1;
		private int tabIndex;
		private int tabEnd;
		private string dir = "";
		private bool inputed = true;

		private void ResetTabComplete()
		{
			tabIndex = 0;
			tabEnd = Rst.Text.Length;
			inputed = false;
		}

		private void RunCmd(string cmd)
		{
			if (cmd == @"cls")
			{
				Action act = () =>
				{
					Rst.Text = "";
					RstLen = 0;

					Proc.StandardInput.WriteLine("");
				};

				Dispatcher.BeginInvoke(act);
			}
			else
			{
				lock (Locker)
				{
					RstLen = Rst.Text.Length; //protect input texts
					CmdRepl = true;
					Proc.StandardInput.WriteLine(cmd);
				}
			}

			CmdList.Add(cmd);
			CmdPos = CmdList.Count - 1;
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		static extern bool AttachConsole(uint dwProcessId);

		[DllImport("kernel32.dll", SetLastError = true)]
		static extern bool SetConsoleCtrlHandler(uint dwProcessId, bool state);

		[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
		static extern bool FreeConsole();

		// Enumerated type for the control messages sent to the handler routine
		enum CtrlTypes : uint
		{
			CTRL_C_EVENT = 0,
			CTRL_BREAK_EVENT,
			CTRL_CLOSE_EVENT,
			CTRL_LOGOFF_EVENT = 5,
			CTRL_SHUTDOWN_EVENT
		}

		[DllImport("kernel32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GenerateConsoleCtrlEvent(CtrlTypes dwCtrlEvent, uint dwProcessGroupId);

		public void SendCtrlC(Process proc)
		{
			FreeConsole();

			//This does not require the console window to be visible.
			if (AttachConsole((uint)proc.Id))
			{
				//Disable Ctrl-C handling for our program
				SetConsoleCtrlHandler(0, true);
				GenerateConsoleCtrlEvent(CtrlTypes.CTRL_C_EVENT, 0);

				// Must wait here. If we don't and re-enable Ctrl-C
				// handling below too fast, we might terminate ourselves.
				//proc.WaitForExit(500);

				Thread.Sleep(100);

				//Re-enable Ctrl-C handling or any subsequently started
				//programs will inherit the disabled state.
				SetConsoleCtrlHandler(0, false);
			}
		}

		private void OnText(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Back && Rst.CaretIndex <= RstLen)
			{
				e.Handled = true;
			}
			else if (Rst.CaretIndex < RstLen)
			{
				Rst.CaretIndex = Rst.Text.Length;
			}
			else if (e.Key == Key.Up)
			{
				if (CmdPos >= 0)
				{
					Rst.Text = Rst.Text.Substring(0, RstLen) + CmdList[CmdPos];
					CmdPos -= 1;
					Rst.Select(Rst.Text.Length, 0);
				}

				e.Handled = true;
			}
			else if (e.Key == Key.Down)
			{
				if (CmdPos < CmdList.Count - 2)
				{
					CmdPos += 1;
					Rst.Text = Rst.Text.Substring(0, RstLen) + CmdList[CmdPos];
					Rst.Select(Rst.Text.Length, 0);
				}

				e.Handled = true;
			}
			else if (e.Key == Key.C && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control)) //Keyboard.IsKeyDown(Key.LeftCtrl)
			{
				SendCtrlC(Proc);

				e.Handled = true;
			}
			else if (e.Key == Key.Tab)
			{
				e.Handled = true;

				if (inputed)
				{
					ResetTabComplete();
				}

				string cmd = Rst.Text.Substring(RstLen, tabEnd - RstLen);

				int pos = cmd.LastIndexOf('"');
				if (pos == -1)
				{
					pos = cmd.LastIndexOf(' ');
				}

				string tabHit = cmd.Substring(pos + 1);

				try
				{
					string AdditionalPath = "\\";

					if (tabHit.LastIndexOf('\\') != -1)
					{
						AdditionalPath += tabHit.Substring(0, tabHit.LastIndexOf('\\'));
						tabHit = tabHit.Substring(tabHit.LastIndexOf('\\') + 1);
					}

					var files = Directory.GetFileSystemEntries(dir + AdditionalPath, tabHit + "*");

					if (files.Length == 0)
					{
						return; //no match
					}

					if (tabIndex >= files.Length)
					{
						tabIndex = 0;
					}

					Rst.Text = Rst.Text.Remove(tabEnd - tabHit.Length);

					string tabFile = files[tabIndex++];
					string tabName = tabFile.Substring(tabFile.LastIndexOf('\\') + 1);
					Rst.AppendText(tabName);
					Rst.Select(Rst.Text.Length, 0);
				}
				catch (ArgumentException ex)
				{
					Debug.WriteLine(ex);
					tabIndex = 0;
				}
			}
			else if (e.Key == Key.Return)
			{
				if (Rst.CaretIndex >= RstLen)
				{
					string cmd = Rst.Text.Substring(RstLen, Rst.Text.Length - RstLen);

					RunCmd(cmd);

					ResetTabComplete();
				}

				e.Handled = true;
			}
			else
			{
				inputed = true;
			}
		}

		private void OnClear(object sender, EventArgs e)
		{
			Rst.Text = "";
			RstLen = 0;
			Proc.StandardInput.WriteLine("");
		}
		private void OnRestart(object sender, EventArgs e)
		{
			Proc.Kill();
			Rst.Text = "";
			RstLen = 0;
			//Restart();
		}

		private void OnSave(object sender, EventArgs e)
		{
			SaveFileDialog saveDlg = new SaveFileDialog
			{
				Filter = "txt文件|*.txt|所有文件|*.*",
				FilterIndex = 2,
				RestoreDirectory = true,
				DefaultExt = ".txt",
				AddExtension = true,
				Title = "Save Cmd Results"
			};

			if (saveDlg.ShowDialog() == true)
			{
				FileStream saveStream = new FileStream(saveDlg.FileName, FileMode.Create);
				byte[] data = new UTF8Encoding().GetBytes(Rst.Text);
				saveStream.Write(data, 0, data.Length);
				saveStream.Flush();
				saveStream.Close();
			}
		}

		private void OnCopy(object sender, RoutedEventArgs e)
		{
			Clipboard.SetText(Rst.SelectedText);
		}

		private void OnLoad(object sender, EventArgs e)
		{
			Init();

			Rst.Focus();
		}
	}
}
