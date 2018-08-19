/*
    Copyright 2014 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
using XTMF;

namespace Tasha.Common
{
    /// <summary>
    ///
    /// </summary>
    public interface IPostIteration : IModule
    {
        /// <summary>
        /// This will be called before the iteration starts
        /// </summary>
        /// <param name="iterationNumber">The iteration that we are able to run</param>
        /// <param name="totalIterations">The total number of iterations tasha will do</param>
        void Execute(int iterationNumber, int totalIterations);

        /// <summary>
        /// Loads the module, letting it know how many iterations there will be
        /// </summary>
        /// <param name="config">The configuration file</param>
        /// <param name="totalIterations">The total number of iterations</param>
        void Load(IConfiguration config, int totalIterations);
    }
}