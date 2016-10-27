using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.Xml;
using System.Reflection;

namespace Humana.H1.JobService
{
	/// <summary>
	/// Summary description for ProjectInstaller.
	/// </summary>
	[RunInstaller(true)]
	public class ProjectInstaller : System.Configuration.Install.Installer
	{
		private System.ServiceProcess.ServiceProcessInstaller serviceProcessInstaller1;
		private System.ServiceProcess.ServiceInstaller serviceInstaller1;
		/// <summary>
		/// Required designer variable.
		/// </summary>

		public ProjectInstaller()
		{
			// This call is required by the Designer.
			InitializeComponent();

			string strServiceName = "";

			try 
			{
				string strExecutingName = Assembly.GetExecutingAssembly().GetName().Name + ".exe";
				string strExecutingPath = Assembly.GetExecutingAssembly().CodeBase.Replace(strExecutingName, "");

				XmlDocument xdoc = new XmlDocument();
				xdoc.Load(strExecutingPath + "Humana.H1.JobService.exe.config");
				strServiceName = xdoc.SelectSingleNode(@"/configuration/appSettings/add[@key='ServiceName']/@value").Value;

			} 
			catch (Exception e)
			{
				throw e;
			}

			if (strServiceName.Length > 0) 
				this.serviceInstaller1.ServiceName = strServiceName;
			else 
				this.serviceInstaller1.ServiceName = "H1JobService";

			// 
			// ProjectInstaller
			// 
			this.Installers.AddRange(new System.Configuration.Install.Installer[] {
																					  this.serviceProcessInstaller1,
																					  this.serviceInstaller1});
		}

		#region Component Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.serviceProcessInstaller1 = new System.ServiceProcess.ServiceProcessInstaller();
			this.serviceInstaller1 = new System.ServiceProcess.ServiceInstaller();
			// 
			// serviceProcessInstaller1
			// 
			this.serviceProcessInstaller1.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
			this.serviceProcessInstaller1.Password = null;
			this.serviceProcessInstaller1.Username = null;
			// 
			// serviceInstaller1
			// 
			this.serviceInstaller1.ServiceName = "Humana.H1.JobService";
			this.serviceInstaller1.StartType = System.ServiceProcess.ServiceStartMode.Automatic;

		}
		#endregion
	}
}
