﻿'''
    Copyright 2014-2018 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
'''

import sys
import os
import glob
import time
import math
import array
import Queue
import inspect
import timeit
import struct
import inro.modeller
import traceback as _traceback
import inro.modeller as _m
from inro.emme.desktop import app as _app
from threading import Thread
import threading
import time
from contextlib import contextmanager

class ProgressTimer(Thread):
    def __init__(self, delegateFunction, XtmfBridge):
        self._stopped = False
        self.delegateFunction = delegateFunction
        self.bridge = XtmfBridge
        Thread.__init__(self)
        self.run = self._run
    
    def _run(self):
        try:
            while not self._stopped:
                progressTuple = self.delegateFunction()
                self.bridge.ReportProgress(float(progressTuple[2] - progressTuple[0]) / float(progressTuple[1] - progressTuple[0]))
                time.sleep(0.01667)
        except:
            # silently fail if we are unable to understand what the progress tuple is doing.
            pass
    
    def stop(self):
        self._stopped = True

# A Stream which redirects print statements to XTMF Console
class RedirectToXTMFConsole:
    def __init__(self, xtmfBridge):
        self.bridge = xtmfBridge
    
    def write(self, data):
        self.bridge.SendPrintSignal(str(data))

#This class is designed to encapsulate async IO writes
class WriteMessageQueue(Thread):
    def __init__(self, bridge, pipeIn):
        Thread.__init__(self)
        self._exit = False
        self.Bridge = bridge
        self.ToXTMF = open(pipeIn, 'wb', 0)
        self.WriteQueue = Queue.Queue()
        self.setDaemon(True)
        return

    def run(self):
        try:
            while not self._exit:
                msg = self.WriteQueue.get()
                self.WriteQueue.task_done()
                if msg is not None:
                    #self.Bridge.WriteToConsole(str(msg))
                    for subMessage in msg:
                        subMessage.tofile(self.ToXTMF)
                else:
                    return
        except:
            self.Bridge.WriteToConsole("We had an exception while writing!")
        return

    def add_message(self, msg):
        self.WriteQueue.put_nowait(msg)
        return

    def kill(self):
        self._exit = True
        self.WriteQueue.put_nowait(None)
        return

class XTMFBridge:
    WriteQueue = None
    """The stream used for getting data from XTMF"""
    FromXTMF = None
    """Our link to the EMME modeller"""
    Modeller = None
    _exit = False
    """The name of the field that XTMF enabled Modeller Tools will use"""
    _XTMFCallParameters = "XTMFCallParameters"
    
    # Message numbers
    """Tell XTMF that we are ready to start accepting messages"""
    SignalStart = 0
    """Tell XTMF that we exited / XTMF is telling us to exit"""
    SignalTermination = 1
    """XTMF is telling us to start up a tool"""
    SignalStartModule = 2
    """Tell XTMF that we have successfully ran the requested tool"""
    SignalRunComplete = 3
    """Tell XTMF that we have had an error when creating the parameters"""
    SignalParameterError = 4
    """Tell XTMF that we have had an error while running the tool"""
    SignalRuntimeError = 5
    """XTMF says we need to clean out the modeller log book"""
    SignalCleanLogbook = 6
    """We say that we need to generate a progress report for XTMF"""
    SignalProgressReport = 7
    """Tell XTMF that we have successfully ran the requested tool"""
    SignalRunCompleteWithParameter = 8
    """XTMF is requesting a check if a Tool namespace exists"""
    SignalCheckToolExists = 9
    """Tell XTMF that we have had an error finding the requested tool"""
    SignalSendToolDoesNotExistsError = 10
    """Tell XTMF that a print statement has been encountered and to write to the Run Console"""
    SignalSendPrintMessage = 11
    """Signal from XTMF to disable writing to logbook"""
    SignalDisableLogbook = 12
    """Signal from XTMF to enable writing to logbook"""
    SignalEnableLogbook = 13    
    """Signal from XTMF to start up a tool using binary parameters"""
    SignalStartModuleBinaryParameters = 14
        
    """Initialize the bridge so that the tools that we run will not accidentally access the standard I/O"""
    def __init__(self):
        self.IOLock = threading.Lock()
        self.CachedLogbookWrite = _m.logbook_write
        self.CachedLogbookTrace = _m.logbook_trace
        self.previous_level = None
        self.FromXTMF = open(pipeOut, 'rb', 0)
        self._oldstdout = sys.stdout
        sys.stdout = RedirectToXTMFConsole(self)
        self.WriteQueue = WriteMessageQueue(self, pipeIn);
        self.WriteQueue.start()
        return
    
    def ReadLEB(self):
        ret = 0
        Continue = True
        Continues = 0
        bitIndex = 0
        while Continue:
            #unsigned array
            byteArray = array.array('B')
            byteArray.fromfile(self.FromXTMF, 1)
            current = byteArray.pop()
            if current < 128:
                Continue = False
            else:
                current -= 128
            # Add together the numbers
            ret = ret + (current << bitIndex)
            bitIndex += 7
            #ret = (ret << 7) + current
        return ret
        
    def ReadString(self):
        length = self.ReadLEB()
        if length <= 0:
            return ""
        try:
            stringArray = array.array('c')
            stringArray.fromfile(self.FromXTMF, length)
            return stringArray.tostring() 
        except:
            return stringArray.tostring()
    
    def ReadInt(self):
        val = struct.unpack('i', self.FromXTMF.read(4))[0]
        return val 
    
    def IsWhitespace(self, c):
        return (c == ' ') or (c == '\t') or (c == '\s')
    
    def CreateTool(self, toolName):
        return self.Modeller.tool(toolName)
    
    def GetToolParameterTypes(self, tool):
        # get the names of the parameters
        parameterNames = inspect.getargspec(tool.__call__)[0][1:]
        ret = []
        for param in parameterNames:
            try:
                paramVar = eval("tool.__class__." + str(param))
            except:
                _m.logbook_write("A parameter with the name '" + param + "' does not exist in the executing EMME tool!  Make sure that the EMME tool defines this attribute as a class variable.")
                self.SendParameterError("A parameter with the name '" + param + "' does not exist in the executing EMME tool!  Make sure that the EMME tool defines this attribute as a class variable.")
                return None
            typeOfParam = paramVar.type
            if typeOfParam == _m.Attribute(float).type:
                ret.append("float")
            elif typeOfParam == _m.Attribute(int).type:
                ret.append("int")
            elif typeOfParam == _m.Attribute(str).type:
                ret.append("string")
            elif typeOfParam == _m.Attribute(bool).type:
                ret.append("bool")
            else:
                _m.logbook_write(param + " uses a type unsupported by the ModellerBridge '" + str(typeOfParam) + "'!")
                self.SendParameterError(param + " uses a type unsupported by the ModellerBridge '" + str(typeOfParam) + "'!")
                return None
        return ret 
    
    def BreakIntoParametersStrings(self, parameterString):
        parameterList = []
        currentlyBuilding = False
        currentParameter = str()
        state = 0
        # execute a FSA to parse the string and extract out the parameters
        for i in range(len(parameterString)):
            c = parameterString[i]
            # initial state, checking to see if the next parameter starts with
            # " or not
            if state == 0:
                if c == '\"':
                    state = 2
                # ignore whitespace until we find the next parameter
                elif not self.IsWhitespace(c):
                    currentlyBuilding = True
                    currentParameter = currentParameter + c
                    state = 1
            # We are currently waiting for a whitespace to end this parameter
            elif state == 1:
                if self.IsWhitespace(c):
                    currentlyBuilding = False
                    parameterList.append(currentParameter)
                    currentParameter = str()
                    state = 0
                else:
                    currentlyBuilding = True
                    currentParameter = currentParameter + c
            # We are currently waiting for a " to end this parameter
            elif state == 2:
                if c == '\"':
                    state = 0
                    currentlyBuilding = False
                    parameterList.append(currentParameter)
                    currentParameter = str()
                else:
                    currentlyBuilding = True
                    currentParameter = currentParameter + c
            else:
                return None
        # Check to see if we were building a parameter, if so add it to our
        # list
        if currentlyBuilding:
            parameterList.append(currentParameter)
        return parameterList
    
    def ConvertIntoTypes(self, parameterList, toolParameterTypes):
        length = len(parameterList)
        if length != len(toolParameterTypes):
            return None
        for i in range(length):
            if toolParameterTypes[i] == "int":
                try:
                    parameterList[i] = int(parameterList[i])
                except:
                    self.SendParameterError("Unable to convert '" + parameterList[i] + "' to an integer!")
                    return None
            elif toolParameterTypes[i] == "string":
                #it is already a string, so we don't need to do anything
                pass
            elif toolParameterTypes[i] == "float":
                try:
                    parameterList[i] = float(parameterList[i])
                except:
                    self.SendParameterError("Unable to convert '" + parameterList[i] + "' to a float!")
                    return None
            elif toolParameterTypes[i] == "bool":
                try:
                    if parameterList[i].lower() in ['true','t','tru','tr']:
                        parameterList[i] = True
                    elif parameterList[i].lower() in ['false','f','fals','fal']:
                        parameterList[i] = False
                    else:
                        self.SendParameterError("Unable to convert '" + parameterList[i] + "' to a bool!")
                except:
                    self.SendParameterError("Unable to convert '" + parameterList[i] + "' to a bool!")
                    return None
            else:
                self.SendParameterError("The type '" + toolParameterTypes[i] + "' is not recognized by this XTMF Bridge!")
                return None
        return parameterList
    
    def BuildCallString(self, toolName, parameterListName, length):
        string = toolName + "("
        for i in range(length):
            if i > 0:
                string += ','
            string += parameterListName + "[" + str(i) + "]"
        string += ")"
        return string
    
    def SendString(self, stringToSend):
        return [array.array('i', [len(stringToSend)]), array.array('c', str(stringToSend))]

    def SendStartSignal(self):
        self.WriteQueue.add_message(self.SendSignal(self.SignalStart))
    
    def SendToolDoesNotExistError(self, namespace):
        self.WriteQueue.add_message(self.SendSignal(self.SignalSendToolDoesNotExistsError) \
            + self.SendString("A tool with the following namespace could not be found: %s" % namespace))            
        return

    def SendParameterError(self, problem):
        self.WriteQueue.add_message( \
            self.SendSignal(self.SignalParameterError) \
            + self.SendString(problem))
        return
        
    def SendRuntimeError(self, problem):
        self.WriteQueue.add_message(\
            self.SendSignal(self.SignalRuntimeError) \
         + self.SendString(problem))
        return
    
    def SendSuccess(self):
        self.WriteQueue.add_message(self.SendSignal(self.SignalRunComplete))
        return
    
    def SendReturnSuccess(self, returnValue):
        self.WriteQueue.add_message(\
            self.SendSignal(self.SignalRunCompleteWithParameter) \
        + self.SendString(str(returnValue)))
        return
    
    def SendSignal(self, signal):
        intArray = array.array('i')
        intArray.append(signal)
        return [intArray]
    
    def SendPrintSignal(self, stringToPrint):
        self.WriteQueue.add_message(\
            self.SendSignal(self.SignalSendPrintMessage) \
        + self.SendString(stringToPrint))
        return

    def ReportProgress(self, progress):
        floatArray = array.array('f')
        floatArray.append(float(progress))
        self.WriteQueue.add_message(self.SendSignal(self.SignalProgressReport) + [floatArray])
        return

    def WriteToConsole(self, msg):
        toWrite = str(msg) + "\r\n"
        try:
            self.IOLock.acquire()
            self._oldstdout.write(toWrite)
            self.IOLock.release()
        except:
            self._oldstdout.write("Exception while writing\r\n")

    def EnsureModellerToolExists(self, macroName):
        for i in range(1, 10):
            if macroName in self.Modeller.tool_namespaces():       
                return True
            time.sleep(1)
        _m.logbook_write("A tool with the following namespace could not be found: %s" % macroName)
        self.SendToolDoesNotExistError(macroName)
        return False

    def ReorderParametersToMatch(self, toolName, expectedParameterNames, sentParameterNames, parameterList):
        #do a quick check to see if everything is in order
        sizeDifference = len(expectedParameterNames) - len(sentParameterNames)
        if sizeDifference < 0:
            #if the call is using less parameters than expected, then find the
            #parameter we are missing
            missing = []
            for param in sentParameterNames:
                if expectedParameterNames.count(param) == 0:
                    missing.append(param)
            self.SendParameterError(str.join("\r\n", ["Unable to find a parameter in the EMME tool '" + toolName + "' called '" + param + "' that was sent!" for param in missing]))
            return False
        elif sizeDifference > 0:
            #if the call has more parameters than the tool
            missing = []
            for param in expectedParameterNames:
                if sentParameterNames.count(param) == 0:
                    missing.append(param)
            self.SendParameterError(str.join("\r\n", ["A parameter called '" + param + "' was not sent while calling the tool '" + toolName + "'!" for param in missing]))
            return False
        #We know we have the right number of parameters now
        for i in range(0, len(expectedParameterNames)):
            if expectedParameterNames[i] != sentParameterNames[i]:
                count = expectedParameterNames.count(sentParameterNames[i])
                if count == 0:
                    self.SendParameterError("Unable to find a parameter in the EMME tool '" + toolName + "' called '" + sentParameterNames[i] + "'!")
                    return False
                else:
                    index = expectedParameterNames.index(sentParameterNames[i])
                    #then we know there is a miss ordering for this parameter we can just swap
                    temp = sentParameterNames[i]
                    sentParameterNames[i] = sentParameterNames[index]
                    sentParameterNames[index] = temp
        return True
    
    def ExecuteModule(self, useBinaryParameters):
        macroName = None
        parameterString = None
        timer = None
        # run the module here
        try:
            #figure out how long the macro's name is
            macroName = self.ReadString()
            if not self.EnsureModellerToolExists(macroName):
                return
            tool = self.CreateTool(macroName)
            toolParameterTypes = self.GetToolParameterTypes(tool)
            if toolParameterTypes == None:
                return
            if useBinaryParameters:
                #Read in the number of strings, one for each parameter
                numberOfParameters = int(self.ReadString())
                sentParameterNames = [self.ReadString() for p in range(0, numberOfParameters)]
                parameterList = [self.ReadString() for p in range(0, numberOfParameters)]
                expectedParameterNames = inspect.getargspec(tool.__call__)[0][1:]
                if not self.ReorderParametersToMatch(macroName, expectedParameterNames, sentParameterNames, parameterList):
                    return
                parameterString = str.join(',', ['{%s:%s}' %(sentParameterNames[p], parameterList[p]) for p in range(0, numberOfParameters)])
            else:
                parameterString = self.ReadString()
                parameterList = self.BreakIntoParametersStrings(parameterString)
            parameterList = self.ConvertIntoTypes(parameterList, toolParameterTypes)
            if parameterList == None:
                _m.logbook_write("We were unable to create the parameters to their given types, or there was the wrong number of arguments for the tool " + macroName + ".")
                _m.logbook_write("The parameter string was \r\n" + parameterString)
                self.SendParameterError("The module \"" + macroName + "\" was executed with the wrong number of arguments or of invalid types.")
                return
            parameterNames = inspect.getargspec(tool.__call__)[0][1:]
            #Do the exec in another namespace
            nameSpace = {'tool':tool, 'parameterNames':parameterNames, 'parameterList':parameterList}
            for i in range(len(parameterList)):
                if toolParameterTypes[i] == "string":
                    toExecute = "tool." + parameterNames[i] + "='" + str(parameterList[i]).replace("\\","\\\\").replace("'","\\'").replace("\"","\\\"") + "'"
                    exec toExecute in nameSpace
                else:
                    exec "tool." + parameterNames[i] + "=" + str(parameterList[i]) in nameSpace
            callString = self.BuildCallString("tool", "parameterList", len(parameterList))
            #Now that everything is ready, attach an instance of ourselves into
            #the tool so they can send progress reports
            tool.XTMFBridge = self
            
            if "percent_completed" in dir(tool):
                timer = ProgressTimer(tool.percent_completed, self)
                timer.start()
            #Execute the tool, getting the return value
            ret = eval(callString, nameSpace, None)
            if timer != None:
                timer.stop()
            nameSpace = None
            if ret == None: 
                self.SendSuccess()
            else:
                self.SendReturnSuccess(ret)
        except Exception, inst:
            if timer != None:
                timer.stop()
            _m.logbook_write("We are in the exception code for ExecuteModule")
            if(macroName != None):
                _m.logbook_write("Macro Name: " + macroName)
            else:
                _m.logbook_write("Macro Name: None")
            if(parameterString != None):
                _m.logbook_write("Parameter : " + parameterString)
            else:
                _m.logbook_write("Parameter : None")
            _m.logbook_write(str(inst))
            _m.logbook_write("Finished dumping exception")

            etype, evalue, etb = sys.exc_info()
            stackList = _traceback.extract_tb(etb)
            msg = "%s: %s\n\nStack trace below:" % (evalue.__class__.__name__, str(evalue))
            stackList.reverse()
            for file, line, func, text in stackList:
                msg += "\n  File '%s', line %s, in %s" % (file, line, func)
            self.SendRuntimeError(msg)
        return
    
    def CleanLogbook(self):
        try:
            projectFile = None
            projectFiles = glob.glob("*.emp")
            if len(projectFiles) > 0:
                projectFile = projectFiles[0]
            if projectFile == None:
                os.chdir("..")
                projectFiles = glob.glob("*.emp")
                if len(projectFiles) > 0:
                    projectFile = projectFiles[0]
            logbookPath = self.Modeller.desktop.modeller_logbook_url
            self.Modeller = None
            self.emmeApplication.close()
            self.emmeApplication = None
            
            time.sleep(10)
            os.remove(logbookPath)
            self.emmeApplication = _app.start_dedicated(visible=False, user_initials="XTMF", project=projectFile)
            self.Modeller = inro.modeller.Modeller(self.emmeApplication)
            self.SendSuccess()
        except Exception, inst:
            self.SendRuntimeError(str(inst))
        return
            
    def SwitchToDatabank(self, emmeApplication, databankName):
        databankName = databankName.lower()
        for db in emmeApplication.data_explorer().databases():
            if db.name().lower() == databankName:
                db.open()
                return
        self.SendRuntimeError("The databank " + databankName + " does not exist!")

    def Run(self, emmeApplication, databankName, performanceMode):
        self.emmeApplication = emmeApplication
        if databankName is not None:
            self.SwitchToDatabank(emmeApplication, databankName)
        self.Modeller = inro.modeller.Modeller(emmeApplication)
        _m.logbook_write("Activated modeller from ModellerBridge for XTMF")
        if performanceMode:
            _m.logbook_write("Performance Testing Activated")
        # tell XTMF that we are ready
        #Check to see if we have a beta version of EMME and if so force the compatibility tests to always pass.
        self.SendStartSignal()
        try:
            while(not self._exit):
                input = self.ReadInt()
                if input == self.SignalTermination:
                    _m.logbook_write("Exiting on termination signal from XTMF")
                    self._exit = True
                elif input == self.SignalStartModule:
                    if performanceMode:
                        t = timeit.Timer(self.ExecuteModule).timeit(1)
                        _m.logbook_write(str(t) + " seconds to execute.")
                    else:
                        self.ExecuteModule(False)
                elif input == self.SignalStartModuleBinaryParameters:
                    if performanceMode:
                        t = timeit.Timer(self.ExecuteModule).timeit(1)
                        _m.logbook_write(str(t) + " seconds to execute.")
                    else:
                        self.ExecuteModule(True)
                elif input == self.SignalCleanLogbook:
                    self.CleanLogbook()
                elif input == self.SignalCheckToolExists:
                    self.CheckToolExists()
                elif input == self.SignalDisableLogbook:
                    self.DisableLogbook()
                elif input == self.SignalEnableLogbook:
                    self.EnableLogbook()
                else:
                    #If we do not understand what XTMF is saying quietly die
                    self._exit = True
                    _m.logbook_write("Exiting on bad input \"" + str(input) + "\"")
                    self.SendSignal(self.SignalTermination)
        finally:
            sys.stdout = self._oldstdout
            print "Closing the EMME application"
            emmeApplication.close()
            print "After closing the EMME application"
            self.WriteQueue.kill()

        return

    def CheckToolExists(self):
        ns = self.ReadString()
        ret = ns in self.Modeller.tool_namespaces()
        if ret == False:
            self.WriteToConsole("Unable to find a tool named " + ns)
            _m.logbook_write("Unable to find a tool named " + ns)
        self.SendReturnSuccess(ret)
        return
    
    def DisableLogbook(self):
        self.previous_level = inro.modeller.logbook_level()
        _m.logbook_level(inro.modeller.LogbookLevel.NONE)
    
    def EnableLogbook(self):
        _m.logbook_level(self.previous_level)
    
#end XTMFBridge

#Get the project file
args = sys.argv 
# 0: This script's location
# 1: Emme project file
# 2: User initials
# 3: Performance flag
# 4: From XTMF -> To EMME
# 5: From EMME -> To XTMF
# 6: Optional; The name of the databank to use inside of the project
projectFile = args[1]
userInitials = args[2]
performancFlag = bool(int(args[3]))
pipeIn = args[4]
pipeOut = args[5]
databank = None
if len(args) > 6:
    databank = args[6]
#sys.stderr.write(args)
print userInitials
print projectFile
TheEmmeEnvironmentXMTF = None
try:
    print "Trying to load project from: " + projectFile
    TheEmmeEnvironmentXMTF = _app.start_dedicated(visible=False, user_initials=userInitials, project=projectFile)
    XTMFBridge().Run(TheEmmeEnvironmentXMTF, databank, performancFlag)
except Exception as e:
    print "Starting to write out error:"
    print dir(e).__class__
    print e.message
    print e.args
