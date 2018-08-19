/*
    Copyright 2017-2018 Travel Modelling Group, University of Toronto for integration into XTMF.

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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XTMF;
using TMG.Emme;
using TMG.Input;
using System.IO;

namespace TMG.EMME.Analysis
{
    [ModuleInformation(Description = "This tool is designed to automate the TMG_Toolbox tool 'tmg.analysis.export_network_tables'.")]
    public sealed class ExportNetworkTables : IEmmeTool
    {
        public string Name { get; set; }

        public float Progress { get; private set; }

        public Tuple<byte, byte, byte> ProgressColour => new Tuple<byte, byte, byte>(50, 150, 50);

        private const string ToolName = "tmg.analysis.export_network_tables";

        [SubModelInformation(Required = true, Description = "The directory to save the data to.")]
        public FileLocation ExportToFolder;

        [RunParameter("Scenario Id", 0, "The scenario in EMME to export the values from.")]
        public int ScenarioId;
        [RunParameter("File Prefix", "", "A prefix to the file names to match '[Prefix]_[FileName]'.  Use this if you are going to store multiple results in the same directory.")]
        public string FilePrefix;
        [RunParameter("Export Nodes", true, "Export the attributes for nodes in the given scenario to the directory.")]
        public bool ExportNodes;
        [RunParameter("Export Links", true, "Export the attributes for links in the given scenario to the directory.")]
        public bool ExportLinks;
        [RunParameter("Export Turns", true, "Export the attributes for turns in the given scenario to the directory.")]
        public bool ExportTurns;
        [RunParameter("Export Lines", true, "Export the attributes for (transit) lines in the given scenario to the directory.")]
        public bool ExportLines;
        [RunParameter("Export Segments", true, "Export the attributes for (transit) segments in the given scenario to the directory.")]
        public bool ExportSegments;

        /// <summary>
        /// Convert the bool to a True/False for python
        /// </summary>
        /// <param name="b">The value to convert</param>
        /// <returns>A true/false string that can be read by python.</returns>
        private static string BoolToString(bool b)
        {
            return b ? "True" : "False";
        }

        public bool Execute(Controller controller)
        {
            Progress = 0.0f;
            if (controller is ModellerController mc)
            {
                var dir = SetupResultDirectory();
                var parmeters = new ModellerControllerParameter[]
                {
                    new ModellerControllerParameter("scenario_id", ScenarioId.ToString()),
                    new ModellerControllerParameter("target_folder", dir.FullName),
                    new ModellerControllerParameter("file_prefix", FilePrefix),
                    new ModellerControllerParameter("export_nodes", BoolToString(ExportNodes)),
                    new ModellerControllerParameter("export_links", BoolToString(ExportLinks)),
                    new ModellerControllerParameter("export_turns", BoolToString(ExportTurns)),
                    new ModellerControllerParameter("export_lines", BoolToString(ExportLines)),
                    new ModellerControllerParameter("export_segments", BoolToString(ExportSegments)),
                };
                string ret = null;
                return mc.Run(this, ToolName, parmeters, (p) => Progress = p, ref ret);
            }
            throw new XTMFRuntimeException(this, $"In {Name}, the EMME controller was not for modeller!");
        }

        /// <summary>
        /// Get the information for the result directory and ensure that it exists.
        /// </summary>
        /// <returns>A reference to the result directory.</returns>
        private DirectoryInfo SetupResultDirectory()
        {
            // Ensure the folder that we are going to save the results to has already been created.
            var dir = new DirectoryInfo(ExportToFolder.GetFilePath());
            if (!dir.Exists)
            {
                dir.Create();
            }
            return dir;
        }

        public bool RuntimeValidation(ref string error)
        {
            if (ScenarioId <= 0)
            {
                error = $"In {Name} the Scenario ID is not set to a valid scenario number.";
                return false;
            }
            return true;
        }
    }
}
