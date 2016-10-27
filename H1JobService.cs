/*
 * 
 * 
 * Last Updated:  
 *      2002-06-25  uhl   No auto scan on startup.  Service name in event log.
 *		2002-06-07  uhl   Selective monitoring of jobs.  Select which jobs you want to monitor through xml config file.
 *		2002-06-03  uhl   Multiple parameters.  UseJobQIDasOnlyParam defintition to run stored procs via OSQL 
 *							and ability to handle input arguments larger than 512 chars.  Added JobOwnsStatuses
 *                          flag to allow other processed to control completion and status writes (sp comes to mind).
 *
 */


/*
 * 
 * 
	Table JobDef stores the job definition.  The following columns are defined:
	
		JobID					Unique ID specifying this job.
		Title					Name for the job
		Description				Short description of the job
		XMLLocation				Location of XML files (dir)
		PDFLocation				Location of PDF files (dir)
		ScriptToCall			Script to run (from point of view of this service)
		MaxTimeToCompletion		Allow this much time for completion (in mSec)
		OnErrorEmail			If an error occurs, email this address
		UseJobQIDasOnlyParam	Use JobQ as param1.  All other params 
		Param1					Additional command line arguments for the script
		Param1Desc				Description of the command line argument
		Param2
		Param2Desc
		...
		Param10
		Param10Desc

	The jobs are stored in the JobQ table...
	
		JobQID
		UserID
		JobID			job defintion ID
		JobSubmitted
		Param1
		Param2
		Param3
		Param4
		Param5
		Param6
		Param7
		Param8
		Param9
		Param10
		JobStarted
		JobEnded
		JobErrorStatus
		JobMessages
		Progress
		SuccessfulCompletion	
		
	Current behavior:
	
		Scan the jobQ table for new entries to run
		If an entry is to be run then issue a command to the OS to run the command
		Wait for completion.
		Output (stdout and stderr) are captured.
		If any output is on stderr then job failed.
		
		If the UseJobQIDasOnlyParam is set to true then the job completion handling is done by the job
		running.  If no status or completion status is written (NULL) then the jobserver will mark these jobs as 
		having failed.
		
		Parameters are now defined 1-10. 
		 
		Parameter processing...
		
			1. Read the jobQ param
			2. If jobQ param is null use the value in jobdef instead.
			3. If the jobdef param is null then it is null unless special code pulls values from properties.
			4. Parameters are automatically wrapped in "'s
			5. Send all parameters up to the first null one.
	
	To Do Done:
	
		x - Generalize param1 and param2 usage. 
		x - Handle existing PDF logic through parameters.	
		x - Be able to handle only specific job types.  Via XML parameters. - Specific to a funtion.

	To Do:

		x  - Add a type to each field so know how to fill. - Fill with temporary created file.
		       use @FieldName to create a file in t
			        specify file name - use folder from jobdef
					specify file name with folder
					specify @xmldocument to have jobserver create a file in location specified by jobdef 
					    and then fill the file name in the parameter list.
		x  - Make so that service name can be specified at runtime instead of by installer - hard coded.
			   Partially done.  Name of program can not be changed and still work - Installer fails to 
			   find the config file.
		  - Rename away from AdaptJobService to more general name. - say like AdaptService
		  - File system watcher needs to tested.
		  
	// Old - for pdf generation param1 is the xml location, param2 is the pdf location

	Query analyzer assistance...
		
		sp_columns jobdef
		sp_columns jobq

		select * from jobdef
		select jobqid,successfulcompletion, jobStarted, jobEnded, * from jobq

		update jobq set progress=null, jobstarted=null, successfulcompletion=null, jobended=null, jobmessages=null, joberrorstatus=null where jobqid=80

		Select count(*) as TotalNumberOpenJobs From JobQ Where JobStarted is null
		select count(*) from RateTableCombined 
		where RAteTableID=428



*/


using System;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.ServiceProcess;
using System.Data.SqlClient;
using System.IO;					  
using System.Text.RegularExpressions; 
using System.Text;
using Humana.H1.JobService.Core.BussinessLogic;
using System.Reflection;


namespace Humana.H1.JobService.EXE
{
	public class JobService : System.ServiceProcess.ServiceBase
	{
		private StringBuilder sbDoTheseJobs = new StringBuilder("", 256);
		private StringBuilder sbDoNotDoTheseJobs = new StringBuilder("", 256);
		private string strServiceName;
		//private bool bAllowToRun = true;

		// public string strServiceName;
		private System.IO.FileSystemWatcher fsw;
		private System.Timers.Timer ActionTimer;
		//protected SqlConnection MyConnection;
		/// <summary> 
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.Container components = null;

		public JobService()
		{
			// This call is required by the Windows.Forms Component Designer.
			InitializeComponent();

            strServiceName = Humana.H1.Common.Caching.H1Properties.GetProperty("ServiceName", "APPLICATION_CONFIG_JOBS");
			if (strServiceName.Length == 0) strServiceName = "JobService";
			this.ServiceName = strServiceName;
		}

		/// <summary>
		/// Main entry point for the process.
		/// </summary>
		static void Main()			// The main entry point for the process
		{
			System.ServiceProcess.ServiceBase[] ServicesToRun;
	
			// More than one user Service may run within the same process. To add
			// another service to this process, change the following line to
			// create a second service object. For example,
			//
			//   ServicesToRun = New System.ServiceProcess.ServiceBase[] {new Service1(), new MySecondUserService()};
			//
			//(new JobService()).OnStart(new string[]{}); 
			ServicesToRun = new System.ServiceProcess.ServiceBase[] { new JobService() };

			System.ServiceProcess.ServiceBase.Run(ServicesToRun);
		}

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.fsw = new System.IO.FileSystemWatcher();
			this.ActionTimer = new System.Timers.Timer();
			((System.ComponentModel.ISupportInitialize)(this.fsw)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.ActionTimer)).BeginInit();
			// 
			// fsw
			// 
			this.fsw.EnableRaisingEvents = true;
			this.fsw.Path = "c:\\";
			this.fsw.Created += new System.IO.FileSystemEventHandler(this.fsw_Created);
			// 
			// ActionTimer
			// 
            this.ActionTimer.Enabled = false;
            this.ActionTimer.AutoReset = true;
			this.ActionTimer.Interval = 15000;
			this.ActionTimer.Elapsed += new System.Timers.ElapsedEventHandler(this.ActionTimer_Elapsed);
			// 
			// JobService
			// 
			this.ServiceName = "H1JobService";
			((System.ComponentModel.ISupportInitialize)(this.fsw)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.ActionTimer)).EndInit();

		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose( bool bDisposing )
		{
			if (bDisposing)
			{
				if (components != null) 
				{
					components.Dispose();
				}
			}
			base.Dispose( bDisposing );
		}

		/// <summary>
		/// Set things in motion so your service can do its work.
		/// </summary>
		protected override void OnStart(string[] args)
		{
			bool bErrorOccurred = false;
			StringBuilder sbMessage = new StringBuilder("", 2048);
		
			try
			{
                JobQManager.Instance.ServiceRunning = true;
				//get version information

				string strVersionInfo = Assembly.GetExecutingAssembly().GetName().Version.ToString();


				sbMessage.AppendFormat("{0} vs {1} - Starting.\n", strServiceName, strVersionInfo);

				// pass in the directory to watch otherwise disable this functionality
				if (args.Length > 0) {

					// set file watcher path
					fsw.Path = args[0].ToString();
					sbMessage.AppendFormat("\nEnabled FileSystemWatch with watch path '{0}'.\n", fsw.Path);

				}else{	

					// disable the file watcher
					fsw.EnableRaisingEvents = false;
					sbMessage.Append("\nDisabled FileSystemWatch since no directory specified on the start parameter.\n");

				}

				SetJobIdentStrings(ref sbMessage);
			
				//set Timer w/default or as specified in .config
                string strServiceTimer = Humana.H1.Common.Caching.H1Properties.GetProperty("strServiceTimer", "APPLICATION_CONFIG_JOBS");
				try
				{
                    int time = 15000;
                    int.TryParse(strServiceTimer, out time);
                    if (time<=0)
                        time = 15000;
                    JobQManager.Instance.SleepInterval = time;
                    ActionTimer.Interval = time;//Convert.ToInt32(strServiceTimer); 
                    ActionTimer.Enabled = true;
					sbMessage.AppendFormat("\nUsing time value (\"strServiceTimer\")=[{0}]", ActionTimer.Interval.ToString());
				}
				catch (Exception e_convert)
				{
					string strErr = e_convert.Message;
					ActionTimer.Interval = 15000;	//15 seconds by default
					sbMessage.AppendFormat("\nUsing time value (\"strServiceTimer\")=[{0}] (default value).", ActionTimer.Interval.ToString());
				}
							
			}
			catch (Exception e)
			{
				string strErr = e.Message;

				sbMessage.AppendFormat("\n\nAn error has occurred. Error message is {0}", e.Message.ToString());
				bErrorOccurred = true;
			}

			EventLog.WriteEntry(strServiceName + ".OnStart", sbMessage.ToString(), bErrorOccurred ? EventLogEntryType.Error : EventLogEntryType.Information );

		}

		/// <summary>
		/// Stop this service.
		/// </summary>
		protected override void OnStop()
		{
			// TODO: Add code here to perform any tear-down necessary to stop your service.
            JobQManager.Instance.ServiceRunning = false;
		}

		private void fsw_Created(object sender, System.IO.FileSystemEventArgs e)
		{
			EventLog.WriteEntry(strServiceName + ".fsw_created", String.Format("File created: {0}.", e.FullPath),EventLogEntryType.Information);
		}

		
		// *** Retrieve list of jobdefs to monitor from config file        
		// *** Does a regex match on key MonitorJobDef.##                  
		// *** Allows use of ALL value to specify all.  All is overriding. 
		// *** Jobdefs with a ! in front are not processed                 
		// *** No key found defaults to all value.                       
		// *** Works by building a SQL where clause to select jobdefIdent values
		private void SetJobIdentStrings(ref StringBuilder sbMessage){
    
				string strJobDefIdent;
				System.Collections.Specialized.NameValueCollection myKeyVals = System.Configuration.ConfigurationSettings.AppSettings;

				sbDoTheseJobs.Length = 0;
				sbDoNotDoTheseJobs.Length = 0;

				bool bAllValueUsed = false;
				for (int i=0; i < myKeyVals.Count; i++) 
				{
					if (Regex.IsMatch(myKeyVals.GetKey(i), "MonitorJobDef\\.*\\d*", RegexOptions.IgnoreCase)) 
					{
						// Cache the value for this key=MonitorJobDef.
						strJobDefIdent = myKeyVals.Get(i).Trim();

						// Is the all keyword used?
						if (Regex.IsMatch(strJobDefIdent, "^ALL$", RegexOptions.IgnoreCase))
						{
							// match all available jobs
							bAllValueUsed = true;
							continue;
						}

						if (strJobDefIdent.StartsWith("!")) 
						{
							// If the jobdef starts with ! then this is a negation.  Do not use this job.
							if (sbDoNotDoTheseJobs.Length > 1) sbDoNotDoTheseJobs.Append(",");
							sbDoNotDoTheseJobs.AppendFormat("{0}", strJobDefIdent.Substring(1, strJobDefIdent.Length - 1));
						}
						else
						{
							// regular job to be checked. 
							if (sbDoTheseJobs.Length > 0) sbDoTheseJobs.Append(",");
							sbDoTheseJobs.AppendFormat("{0}", strJobDefIdent);
						}

					}
				}

				string strMonitorMessage;

				if (sbDoTheseJobs.Length > 0 && !bAllValueUsed) 
				{
					strMonitorMessage = string.Format("\nService will monitor and run entries in Jobq with JobDefIdent matching...{0}", sbDoTheseJobs.ToString());
                    //sbDoTheseJobs.Insert(0, "j.JobDefIdent in ("); 
                    //sbDoTheseJobs.Append(")");
				} 
				else 
				{
					strMonitorMessage = "\nService will monitor and run all Jobq types";
                    //sbDoTheseJobs.Append("1=1");
				}

				if (sbDoNotDoTheseJobs.Length > 0) 
				{
					strMonitorMessage += string.Format(" but will not run entries in Jobq with JobDefIdent matching...{0}", sbDoNotDoTheseJobs.ToString());
                    //sbDoTheseJobs.Append(" AND j.JobDefIdent NOT IN ("); 
                    //sbDoTheseJobs.Append(sbDoNotDoTheseJobs.ToString());
                    //sbDoTheseJobs.Append(")");
				}

				sbMessage.Append(strMonitorMessage);
				sbMessage.Append("\n");
		}

		private void ActionTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			JobQManager.Instance.ProcessOutStandingJobs(ServiceName,sbDoTheseJobs.ToString(),sbDoNotDoTheseJobs.ToString());
		}
	}
}
