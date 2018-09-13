/*
    Copyright 2014-2016 Travel Modelling Group, Department of Civil Engineering, University of Toronto

    This file is part of XTMF.

    XTMF is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using XTMF;

namespace TMG.Emme
{
    public sealed class ModellerController : Controller
    {
        /// <summary>
        /// Tell the bridge to clean out the modeller's logbook
        /// </summary>
        private const int SignalCleanLogbook = 6;

        /// <summary>
        /// We receive this error if the bridge can not get the parameters to match
        /// the selected tool
        /// </summary>
        private const int SignalParameterError = 4;

        /// <summary>
        /// Receive a signal that contains a progress report
        /// </summary>
        private const int SignalProgressReport = 7;

        /// <summary>
        /// We receive this when the Bridge has completed its module run
        /// </summary>
        private const int SignalRunComplete = 3;

        /// <summary>
        /// We receive this when the Bridge has completed its module run and that there is a return value as well
        /// </summary>
        private const int SignalRunCompleteWithParameter = 8;

        /// <summary>
        /// We revive this error if the tool that we tell the bridge to run throws a
        /// runtime exception that is not handled within the tool
        /// </summary>
        private const int SignalRuntimeError = 5;

        /// <summary>
        /// We will receive this from the ModellerBridge
        /// when it is ready to start processing
        /// </summary>
        private const int SignalStart = 0;

        /// <summary>
        /// We will send this signal when we want to start to run a new module
        /// </summary>
        private const int SignalStartModule = 2;

        /// <summary>
        /// We will send this signal when we want to start to run a new module with binary parameters
        /// </summary>
        private const int SignalStartModuleBinaryParameters = 14;

        /// <summary>
        /// This is the message that we will send when it is time to shutdown the bridge.
        /// If we receive it, then we know that the bridge is in a panic and has exited
        /// </summary>
        private const int SignalTermination = 1;

        /// <summary>
        /// Signal the bridge to check if a tool namespace exists
        /// </summary>
        private const int SignalCheckToolExists = 9;

        /// <summary>
        /// Signal from the bridge throwing an exception that the tool namespace could not be found
        /// </summary>
        private const int SignalToolDoesNotExistError = 10;

        /// <summary>
        /// Signal from the bridge that a print statement has been called, to write to the XTMF Console
        /// </summary>
        private const int SignalSentPrintMessage = 11;

        private const int SignalDisableLogbook = 12;

        private const int SignalEnableLogbook = 13;

        private NamedPipeServerStream _pipeFromEMME;
        private NamedPipeServerStream _pipeToEMME;

        /// <summary>
        /// </summary>
        /// <param name="projectFile"></param>
        /// <param name="performanceAnalysis"></param>
        /// <param name="userInitials"></param>
        public ModellerController(IModule module, string projectFile, string databank = null, bool performanceAnalysis = false, string userInitials = "XTMF")
        {
            if (!projectFile.EndsWith(".emp") | !File.Exists(projectFile))
            {
                throw new XTMFRuntimeException(module, AddQuotes(projectFile) + " is not an existing Emme project file (*.emp)");
            }

            //Python invocation command:
            //[FullPath...python.exe] -u [FullPath...ModellerBridge.py] [FullPath...EmmeProject.emp] [User initials] [[Performance (optional)]] 

            // Get the path of the Python executable
            string emmePath = Environment.GetEnvironmentVariable("EMMEPATH");
            if (String.IsNullOrWhiteSpace(emmePath))
            {
                throw new XTMFRuntimeException(module, "Please make sure that EMMEPATH is on the system environment variables!");
            }
            string pythonDirectory = Path.Combine(emmePath, FindPythonDirectory(module, emmePath));
            string pythonPath = Path.Combine(pythonDirectory, @"python.exe");
            if(!File.Exists(pythonPath))
            {
                pythonPath = AddQuotes(Path.Combine(pythonDirectory, @"bin/python2.7"));
            }
            string pythonLib = Path.Combine(pythonDirectory, "lib");

            // Get the path of ModellerBridge
            // Learn where the modules are stored so we can find the python script
            // The Entry assembly will be the XTMF.GUI or XTMF.RemoteClient
            string programPath = Path.GetFullPath(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            // Since the modules are always located in the ~/Modules subdirectory for XTMF,
            // we can just go in there to find the script
            var modulesDirectory = Path.Combine(programPath, "Modules");

            // Setup up the new process
            Emme = new Process();
            ProcessStartInfo startInfo;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // When EMME is installed it will link the .py to their python interpreter properly
                string argumentString = AddQuotes(Path.Combine(modulesDirectory, "ModellerBridge-Windows.py"));
                string pinLocal = Guid.NewGuid().ToString();
                string poutLocal = Guid.NewGuid().ToString();
                _pipeFromEMME = new NamedPipeServerStream(pinLocal, PipeDirection.In);
                _pipeToEMME = new NamedPipeServerStream(poutLocal, PipeDirection.Out);
                var pipeIn = "\\\\.\\pipe\\" + pinLocal;
                var pipeOut = "\\\\.\\pipe\\" + poutLocal;
                //The first argument that gets passed into the Bridge is the name of the Emme project file
                argumentString += " " + AddQuotes(projectFile) + " " + userInitials + " " + (performanceAnalysis ? 1 : 0) + " \"" + pipeIn + "\"" + " \"" + pipeOut + "\"";
                if (!String.IsNullOrWhiteSpace(databank))
                {
                    argumentString += " " + AddQuotes(databank);
                }
                //Console.WriteLine(AddQuotes(pythonPath) + " " + argumentString);
                startInfo = new ProcessStartInfo(AddQuotes(pythonPath), "-u " + argumentString);
                startInfo.EnvironmentVariables["PATH"] += ";" + pythonLib + ";" + Path.Combine(emmePath, "programs");
            }
            else
            {
                string argumentString = AddQuotes(Path.Combine(modulesDirectory, "ModellerBridge-Linux.py"));
                var pipeIn = Path.Combine(Environment.CurrentDirectory, Guid.NewGuid().ToString());
                var pipeOut = Path.Combine(Environment.CurrentDirectory, Guid.NewGuid().ToString());
                _pipeFromEMME = new NamedPipeServerStream(pipeIn, PipeDirection.In, 1);
                _pipeToEMME = new NamedPipeServerStream(pipeOut, PipeDirection.Out, 1);
                //The first argument that gets passed into the Bridge is the name of the Emme project file
                argumentString += " " + AddQuotes(projectFile) + " " + userInitials + " " + (performanceAnalysis ? 1 : 0) + " \"" + pipeIn + "\"" + " \"" + pipeOut + "\"";
                if (!String.IsNullOrWhiteSpace(databank))
                {
                    argumentString += " " + AddQuotes(databank);
                }
                startInfo = new ProcessStartInfo("xvfb-run", "-a " + AddQuotes(pythonPath) + " " + argumentString);
            }

            Emme.StartInfo = startInfo;
            Emme.StartInfo.CreateNoWindow = true;
            Emme.StartInfo.UseShellExecute = false;
            Emme.StartInfo.RedirectStandardInput = false;
            Emme.StartInfo.RedirectStandardOutput = false;
            Emme.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            Emme.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
            //Start the new process
            try
            {
                Emme.Start();
            }
            catch (Exception e)
            {
                throw new XTMFRuntimeException(module, "Unable to create a bridge to EMME to " + AddQuotes(projectFile) + "!\r\n" + e.Message + "\r\n" + e.StackTrace);
            }
            _pipeFromEMME.WaitForConnection();
            _pipeToEMME.WaitForConnection();
            WaitForStartSignal(module);
            ToEmme = null;
        }

        ~ModellerController()
        {
            Dispose(true);
        }

        /// <summary>
        /// Try to clean out the Emme Modeller's log-book
        /// </summary>
        /// <returns>If we successfully cleaned the log book</returns>
        public bool CleanLogbook(IModule module)
        {
            lock (this)
            {
                try
                {
                    BinaryWriter writer = new BinaryWriter(_pipeToEMME, Encoding.UTF8, true);
                    writer.Write(SignalCleanLogbook);
                    writer.Flush();
                    // now that we have setup the macro, we can force the writer out of scope
                    string unused = null;
                    return WaitForEmmeResponse(module, ref unused, null);
                }
                catch (EndOfStreamException)
                {
                    throw new XTMFRuntimeException(module, "We were unable to communicate with EMME.  Please make sure you have an active EMME license.  Sometimes rebooting has helped fix this bug.");
                }
                catch (IOException)
                {
                    return false;
                }
            }
        }

        private bool WaitForStartSignal(IModule module)
        {
            // now we need to wait
            try
            {
                while (true)
                {
                    BinaryReader reader = new BinaryReader(_pipeFromEMME, Encoding.UTF8, true);
                    int result = reader.ReadInt32();
                    switch (result)
                    {
                        case SignalStart:
                            {
                                return true; 
                            }
                        default:                        
                            {
                                throw new XTMFRuntimeException(module, "Unknown message passed back from the EMME ModellerBridge.  Signal number " + result);
                            }
                    }
                }
            }
            catch (EndOfStreamException)
            {
                throw new XTMFRuntimeException(module, "We were unable to communicate with EMME.  Please make sure you have an active EMME license.  If the problem persists, sometimes rebooting has helped fix this issue with EMME.");
            }
            catch (IOException e)
            {
                throw new XTMFRuntimeException(module, "I/O Connection with EMME ended while waiting for data, with:\r\n" + e.Message);
            }
        }

        private string ReadString(IModule module, BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if(length < 0)
            {
                throw new XTMFRuntimeException(module, "Received a negative length for a string coming across the bridge!");
            }
            char[] ret = new char[length];
            int pos = 0;
            while(pos < length)
            {
                pos += reader.Read(ret, pos, length - pos);
            }
            return new string(ret);
        }

        private bool WaitForEmmeResponse(IModule module, ref string returnValue, Action<float> updateProgress)
        {
            // now we need to wait
            try
            {
                string toPrint;
                while (true)
                {
                    BinaryReader reader = new BinaryReader(_pipeFromEMME, Encoding.UTF8, true);
                    int result = reader.ReadInt32();
                    switch (result)
                    {
                        case SignalStart:
                            {
                                continue;
                            }
                        case SignalRunComplete:
                            {
                                return true;
                            }
                        case SignalRunCompleteWithParameter:
                            {
                                returnValue = ReadString(module, reader);
                                return true;
                            }
                        case SignalTermination:
                            {
                                throw new XTMFRuntimeException(module, "The EMME ModellerBridge panicked and unexpectedly shutdown.");
                            }
                        case SignalParameterError:
                            {
                                throw new EmmeToolParameterException(module, "EMME Parameter Error: " + ReadString(module, reader));
                            }
                        case SignalRuntimeError:
                            {
                                throw new EmmeToolRuntimeException(module, "EMME Runtime " + ReadString(module, reader));
                            }
                        case SignalToolDoesNotExistError:
                            {
                                throw new EmmeToolCouldNotBeFoundException(module, ReadString(module, reader));
                            }
                        case SignalSentPrintMessage:
                            {
                                toPrint = ReadString(module, reader);
                                Console.Write(toPrint);
                                break;
                            }
                        case SignalProgressReport:
                            {
                                var progress = reader.ReadSingle();
                                updateProgress?.Invoke(progress);
                                break;
                            }
                        default:
                            {
                                Console.WriteLine("An unknown message was passed back through the bridge!");
                                throw new XTMFRuntimeException(module, "Unknown message passed back from the EMME ModellerBridge.  Signal number " + result);
                            }
                    }
                }
            }
            catch (EndOfStreamException)
            {
                throw new XTMFRuntimeException(module, "We were unable to communicate with EMME.  Please make sure you have an active EMME license.  If the problem persists, sometimes rebooting has helped fix this issue with EMME.");
            }
            catch (IOException e)
            {
                throw new XTMFRuntimeException(module, "I/O Connection with EMME ended while waiting for data, with:\r\n" + e.Message);
            }
        }

        public bool CheckToolExists(IModule module, string toolNamespace)
        {
            lock (this)
            {
                try
                {
                    EnsureWriteAvailable(module);
                    BinaryWriter writer = new BinaryWriter(_pipeToEMME, Encoding.UTF8, true);
                    writer.Write(SignalCheckToolExists);
                    writer.Write(toolNamespace);
                    writer.Flush();
                }
                catch (IOException e)
                {
                    throw new XTMFRuntimeException(module, "I/O Connection with EMME while sending data, with:\r\n" + e.Message);
                }
                // now we need to wait
                string unused = null;
                return WaitForEmmeResponse(module, ref unused, null);
            }
        }

        public override bool Run(IModule module, string macroName, string arguments)
        {
            string unused = null;
            return Run(module, macroName, arguments, null, ref unused);
        }

        public bool Run(IModule module, string macroName, string arguments, ref string returnValue)
        {
            return Run(module, macroName, arguments, null, ref returnValue);
        }

        public bool Run(IModule module, string macroName, string arguments, Action<float> progressUpdate, ref string returnValue)
        {
            lock (this)
            {
                try
                {
                    EnsureWriteAvailable(module);
                    // clear out all of the old input before starting
                    BinaryWriter writer = new BinaryWriter(_pipeToEMME, Encoding.UTF8, true);
                    writer.Write(SignalStartModule);
                    writer.Write(macroName);
                    writer.Write(arguments);
                    writer.Flush();
                }
                catch (IOException e)
                {
                    throw new XTMFRuntimeException(module, "I/O Connection with EMME while sending data, with:\r\n" + e.Message);
                }
                return WaitForEmmeResponse(module, ref returnValue, progressUpdate);
            }
        }

        /// <summary>
        /// Throws an exception if the bridge has been disposed
        /// </summary>
        private void EnsureWriteAvailable(IModule module)
        {
            if (_pipeToEMME == null)
            {
                throw new XTMFRuntimeException(module, "EMME Bridge was invoked even though it has already been disposed.");
            }
        }

        public bool Run(IModule module, string macroName, ModellerControllerParameter[] arguments)
        {
            string unused = null;
            return Run(module, macroName, arguments, null, ref unused);
        }

        public bool Run(IModule module, string macroName, ModellerControllerParameter[] arguments, ref string returnValue)
        {
            return Run(module, macroName, arguments, null, ref returnValue);
        }

        public bool Run(IModule module, string macroName, ModellerControllerParameter[] arguments, Action<float> progressUpdate, ref string returnValue)
        {
            lock (this)
            {
                try
                {
                    EnsureWriteAvailable(module);
                    // clear out all of the old input before starting
                    BinaryWriter writer = new BinaryWriter(_pipeToEMME, Encoding.UTF8, true);
                    writer.Write(SignalStartModuleBinaryParameters);
                    writer.Write(macroName);
                    if (arguments != null)
                    {
                        writer.Write(arguments.Length.ToString());
                        for (int i = 0; i < arguments.Length; i++)
                        {
                            writer.Write(arguments[i].Name);
                        }
                        for (int i = 0; i < arguments.Length; i++)
                        {
                            writer.Write(arguments[i].Value);
                        }
                    }
                    else
                    {
                        writer.Write("0");
                    }
                    writer.Flush();
                }
                catch (IOException e)
                {
                    throw new XTMFRuntimeException(module, "I/O Connection with EMME while sending data, with:\r\n" + e.Message);
                }
                return WaitForEmmeResponse(module, ref returnValue, progressUpdate);
            }
        }

        private string AddQuotes(string fileName)
        {
            return String.Concat("\"", fileName, "\"");
        }

        protected override void Dispose(bool finalizer)
        {
            lock (this)
            {
                if (_pipeFromEMME != null && _pipeFromEMME.IsConnected)
                {
                    _pipeFromEMME.Dispose();
                    _pipeFromEMME = null;
                }

                if (_pipeToEMME != null && _pipeToEMME.IsConnected)
                {
                    // Send our termination message first
                    try
                    {
                        BinaryWriter writer = new BinaryWriter(_pipeToEMME);
                        writer.Write(SignalTermination);
                        writer.Flush();
                    }
                    // Argument exception occurs if the stream is not writable
                    catch (ArgumentException) { }
                    catch (IOException) { }
                    // after our message has been sent then we can go and kill the stream
                    _pipeToEMME?.Dispose();
                    _pipeToEMME = null;
                }
            }
        }

        private string FindPythonDirectory(IModule module, string emmePath)
        {
            if (!Directory.Exists(emmePath))
            {
                throw new XTMFRuntimeException(module, "We were unable to find an EMME installation in the directory named '" + emmePath + "'!\r\nIf you have just installed EMME please reboot your system.");
            }
            foreach (var dir in Directory.GetDirectories(emmePath))
            {
                var localName = Path.GetFileName(dir);
                if (localName != null && localName.StartsWith("Python"))
                {
                    var remainder = localName.Substring("Python".Length);
                    if (remainder.Length > 0 && char.IsDigit(remainder[0]))
                    {
                        return localName;
                    }
                }
            }
            throw new XTMFRuntimeException(module, "We were unable to find a version of python inside of EMME!");
        }
    }
}