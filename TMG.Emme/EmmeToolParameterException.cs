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
using System;

namespace TMG.Emme
{
    [Serializable]
    public class EmmeToolParameterException : XTMFRuntimeException
    {
        [System.Obsolete("Use EmmeToolParameterException(IModule module, string message) instead.")]
        public EmmeToolParameterException(string description)
           : base(description)
        {
        }

        public EmmeToolParameterException(IModule module, string description)
            : base(module, description)
        {
        }
    }
}