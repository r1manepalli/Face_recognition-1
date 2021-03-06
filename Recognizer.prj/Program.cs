﻿using System;
using Autofac;
using Mallenom.AppServices;
using Recognizer.AppServices;
using Recognizer.Logs;

namespace Recognizer
{
	static class Program
	{
		[STAThread]
		static void Main()
		{
			var appServices = new AppCriticalServices();
			using(var container = RegistrationServices.CreateContainer(appServices))
			using(var appBootstrapper = container.Resolve<IAppBootstrapper>())
			{
				appBootstrapper.Container = container;

				var appConfiguration = container.Resolve<IApplicationConfiguration>();
				appConfiguration.Load();
				// Fix-me: удалите
				//var tpu = container.Resolve<TestParametersUser>();
				appBootstrapper.Run();
				appConfiguration.Save();
			}
		}
	}
}
