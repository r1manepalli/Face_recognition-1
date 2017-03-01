﻿using System;
using System.Windows.Forms;
using Autofac;
using Mallenom.Video;
using Recognizer.AppServices;
using static Recognizer.Logs.LoggingService;

namespace Recognizer
{
	public sealed class AppBootstrapper : IAppBootstrapper
	{
		public AppBootstrapper()
		{
		}

		public IContainer Container { get; set; }

		public void Run()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm(Container));
		}

		public void Dispose()
		{

		}
	}
}
