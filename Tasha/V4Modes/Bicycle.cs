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
using Datastructure;
using System;
using Tasha.Common;
using TMG;
using XTMF;

namespace Tasha.V4Modes
{
    /// <summary>
    /// Provides the ability to ride a bike
    /// </summary>
    [ModuleInformation(Description =
        @"This module is designed to implement the Bicycle mode for GTAModel V4.0+.")]
    public sealed class Bicycle : ITashaMode, IIterationSensitive, ITourDependentMode
    {
        [RootModule]
        public ITashaRuntime Root;

        [RunParameter("Constant", 0f, "The constant factor applied for the bicycle mode")]
        public float Constant;

        [RunParameter("MarketFlag", 0f, "Added to the utility if the trip's purpose is market.")]
        public float MarketFlag;

        [RunParameter("OtherFlag", 0f, "Added to the utility if the trip's purpose is 'other'.")]
        public float OtherFlag;

        [RunParameter("SchoolFlag", 0f, "Added to the utility if the trip's purpose is 'School'.")]
        public float SchoolFlag;

        [RunParameter("IntrazonalConstant", 0f, "The factor applied for being an intrazonal trip")]
        public float IntrazonalConstant;

        [RunParameter("Max Travel Distance", 12000f, "The largest distance a person is allowed to bike (meters)")]
        public float MaxTravelDistance;

        [RunParameter("ProfessionalTimeFactor", 0f, "The TimeFactor applied to the person type.")]
        public float ProfessionalTimeFactor;
        [RunParameter("GeneralTimeFactor", 0f, "The TimeFactor applied to the person type.")]
        public float GeneralTimeFactor;
        [RunParameter("SalesTimeFactor", 0f, "The TimeFactor applied to the person type.")]
        public float SalesTimeFactor;
        [RunParameter("ManufacturingTimeFactor", 0f, "The TimeFactor applied to the person type.")]
        public float ManufacturingTimeFactor;
        [RunParameter("StudentTimeFactor", 0f, "The TimeFactor applied to the person type.")]
        public float StudentTimeFactor;
        [RunParameter("NonWorkerStudentTimeFactor", 0f, "The TimeFactor applied to the person type.")]
        public float NonWorkerStudentTimeFactor;

        [DoNotAutomate]
        public IVehicleType VehicleType = null;

        [RunParameter("YoungAdultFlag", 0f, "The factor applied for being a young adult")]
        public float YoungAdultFlag;

        [RunParameter("YouthFlag", 0f, "The factor applied for the being a youth")]
        public float YouthFlag;

        [RunParameter("ChildFlag", 0f, "The factor applied for the being a child")]
        public float ChildFlag;

        [RunParameter("ProfessionalConstant", 0f, "The constant applied to the person type.")]
        public float ProfessionalConstant;
        [RunParameter("GeneralConstant", 0f, "The constant applied to the person type.")]
        public float GeneralConstant;
        [RunParameter("SalesConstant", 0f, "The constant applied to the person type.")]
        public float SalesConstant;
        [RunParameter("ManufacturingConstant", 0f, "The constant applied to the person type.")]
        public float ManufacturingConstant;
        [RunParameter("StudentConstant", 0f, "The constant applied to the person type.")]
        public float StudentConstant;
        [RunParameter("NonWorkerStudentConstant", 0f, "The constant applied to the person type.")]
        public float NonWorkerStudentConstant;

        [SubModelInformation(Required = false, Description = "Augments for utility by time period, zone to zone.")]
        public TimePeriodMatrix UtilityAugmentation;

        private SparseArray<IZone> _zoneSystem;

        private float AvgTravelSpeed;

        [RunParameter("BikeShare Zones", "", "A set of zones that will allow a bike trip without requiring the bike across the full tour.")]
        public RangeSet BikeShareZones;

        /// <summary>
        /// Avg speed of riding a bike
        /// </summary>
        [RunParameter("Average Speed", 15.0f, "The average travel speed in KM/H")]
        public float AvgTravelSpeedInKMperHour
        {
            get
            {
                return AvgTravelSpeed / 1000 * 60; //now m/min
            }

            set
            {
                AvgTravelSpeed = value * 1000 / 60; //now m/min
            }
        }

        [Parameter("Demographic Category Feasible", 1f, "(Automated by IModeParameterDatabase)\r\nIs the currently processing demographic category feasible?")]
        public float CurrentlyFeasible { get; set; }

        [RunParameter("Mode Name", "Bicycle", "The name of the mode")]
        public string ModeName { get; set; }

        /// <summary>
        ///
        /// </summary>
        public string Name
        {
            get;
            set;
        }

        public string NetworkType
        {
            get;
            set;
        }

        /// <summary>
        ///
        /// </summary>
        public bool NonPersonalVehicle
        {
            get { return true; }
        }

        [RunParameter("Observed Signature Code", 'A', "The character code used for model output.")]
        public char ObservedMode
        {
            get;
            set;
        }

        /// <summary>
        ///
        /// </summary>
        public char OutputSignature
        {
            get;
            set;
        }

        public float Progress
        {
            get { return 0; }
        }

        public Tuple<byte, byte, byte> ProgressColour
        {
            get { return new Tuple<byte, byte, byte>(100, 200, 100); }
        }


        /// <summary>
        ///
        /// </summary>
        [DoNotAutomate]
        public IVehicleType RequiresVehicle
        {
            get { return VehicleType; }
        }

        [RunParameter("Variance Scale", 1.0f, "The scale for variance used for variance testing.")]
        public double VarianceScale
        {
            get;
            set;
        }

        /// <summary>
        /// The V for a trip whose mode is bike
        /// </summary>
        /// <param name="trip">The trip</param>
        /// <returns>The V for this trip</returns>
        public double CalculateV(ITrip trip)
        {
            float v = 0;
            var oZone = trip.OriginalZone;
            var dZone = trip.DestinationZone;
            var time = trip.ActivityStartTime;
            ITashaPerson person = trip.TripChain.Person;
            GetPersonVariables(person, out float timeFactor, out float constant);
            v += constant;
            v += UtilityAugmentation?.GetValueFromFlat(time, _zoneSystem.GetFlatIndex(oZone.ZoneNumber), _zoneSystem.GetFlatIndex(dZone.ZoneNumber)) ?? 0.0f;
            if (trip.OriginalZone == trip.DestinationZone)
            {
                v += IntrazonalConstant;
            }
            v += timeFactor * TravelTime(oZone, dZone, time).ToMinutes();
            if (person.Youth)
            {
                v += YouthFlag;
            }
            if (person.YoungAdult)
            {
                v += YoungAdultFlag;
            }
            return v;
        }

        private void GetPersonVariables(ITashaPerson person, out float time, out float constant)
        {
            if (person.EmploymentStatus == TTSEmploymentStatus.FullTime)
            {
                switch (person.Occupation)
                {
                    case Occupation.Professional:
                        constant = ProfessionalConstant;
                        time = ProfessionalTimeFactor;
                        return;
                    case Occupation.Office:
                        constant = GeneralConstant;
                        time = GeneralTimeFactor;
                        return;
                    case Occupation.Retail:
                        constant = SalesConstant;
                        time = SalesTimeFactor;
                        return;
                    case Occupation.Manufacturing:
                        constant = ManufacturingConstant;
                        time = ManufacturingTimeFactor;
                        return;
                }
            }
            switch (person.StudentStatus)
            {
                case StudentStatus.FullTime:
                case StudentStatus.PartTime:
                    constant = StudentConstant;
                    time = StudentTimeFactor;
                    return;
            }
            if (person.EmploymentStatus == TTSEmploymentStatus.PartTime)
            {
                switch (person.Occupation)
                {
                    case Occupation.Professional:
                        constant = ProfessionalConstant;
                        time = ProfessionalTimeFactor;
                        return;
                    case Occupation.Office:
                        constant = GeneralConstant;
                        time = GeneralTimeFactor;
                        return;
                    case Occupation.Retail:
                        constant = SalesConstant;
                        time = SalesTimeFactor;
                        return;
                    case Occupation.Manufacturing:
                        constant = ManufacturingConstant;
                        time = ManufacturingTimeFactor;
                        return;
                }
            }
            constant = NonWorkerStudentConstant;
            time = NonWorkerStudentTimeFactor;
        }

        /// <summary>
        /// Calculate the utility of moving between and origin and a destination
        /// with a disregard for the person taking it
        /// </summary>
        /// <param name="origin">Where they start</param>
        /// <param name="destination">where they go</param>
        /// <param name="time">What time the trip started at</param>
        /// <returns>The number of minutes it takes</returns>
        public float CalculateV(IZone origin, IZone destination, Time time)
        {
            float v = 0;
            v += Constant;
            if (origin.ZoneNumber == destination.ZoneNumber)
            {
                v += IntrazonalConstant;
            }
            return v;
        }

        /// <summary>
        /// Returns the cost of riding a bike
        /// </summary>
        /// <param name="origin">The origin zone</param>
        /// <param name="destination">The destination zone</param>
        /// <param name="time"></param>
        /// <returns>the cost</returns>
        public float Cost(IZone origin, IZone destination, Time time)
        {
            return 0;
        }

        public bool Feasible(IZone origin, IZone destination, Time timeOfDay)
        {
            return Root.ZoneSystem.Distances[origin.ZoneNumber, destination.ZoneNumber] <= MaxTravelDistance;
        }

        /// <summary>
        /// Checking Bike Feasibility
        /// </summary>
        /// <param name="trip">The Trip</param>
        /// <returns>is it Feasible?</returns>
        public bool Feasible(ITrip trip)
        {
            return Root.ZoneSystem.Distances[trip.OriginalZone.ZoneNumber, trip.DestinationZone.ZoneNumber] <= MaxTravelDistance;
        }

        /// <summary>
        /// Checking feasibility of entire trip chain for a bike
        /// </summary>
        /// <param name="tripChain">The Trip Chain to test feasibility on</param>
        /// <returns>is this trip chain feasible?</returns>
        public bool Feasible(ITripChain tripChain)
        {
            var trips = tripChain.Trips;
            var lastPlace = trips[0].OriginalZone;
            var homeZone = lastPlace;
            bool lastMadeWithBike = true;
            bool firstWasBike = trips[0].Mode == this;
            bool anyBike = false;
            for (int i = 0; i < trips.Count; i++)
            {
                var trip = trips[i];
                if (trip.Mode == this)
                {
                    var oZone = trip.OriginalZone;
                    if (oZone != lastPlace)
                    {
                        // See if this could be a bike-share trip
                        if (BikeShareZones.Contains(oZone.ZoneNumber) && BikeShareZones.Contains(trip.DestinationZone.ZoneNumber))
                        {
                            lastMadeWithBike = false;
                            continue;
                        }
                        return false;
                    }
                    lastPlace = trip.DestinationZone;
                    lastMadeWithBike = true;
                    anyBike = true;
                }
                else
                {
                    lastMadeWithBike = false;
                }
            }
            return !anyBike | (firstWasBike & lastPlace == homeZone & lastMadeWithBike);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="observedMode"></param>
        /// <returns></returns>
        public bool IsObservedMode(char observedMode)
        {
            return (observedMode == ObservedMode);
        }

        public void IterationEnding(int iterationNumber, int maxIterations)
        {
            UtilityAugmentation?.UnloadData();
        }

        public void IterationStarting(int iterationNumber, int maxIterations)
        {
            _zoneSystem = Root.ZoneSystem.ZoneArray;
            UtilityAugmentation?.LoadData();
        }

        /// <summary>
        /// This is called before the start method as a way to pre-check that all of the parameters that are selected
        /// are in fact valid for this module.
        /// </summary>
        /// <param name="error">A string that should be assigned a detailed error</param>
        /// <returns>If the validation was successful or if there was a problem</returns>
        public bool RuntimeValidation(ref string error)
        {
            return true;
        }

        /// <summary>
        /// The Travel time from an origin to a zone using a bike
        /// </summary>
        /// <param name="origin">The origin zone</param>
        /// <param name="destination">The destination zone</param>
        /// <param name="time">The time of day</param>
        /// <returns>The travel time</returns>
        public Time TravelTime(IZone origin, IZone destination, Time time)
        {
            double distance = origin == destination ? origin.InternalDistance : Root.ZoneSystem.Distances[origin.ZoneNumber, destination.ZoneNumber];
            Time ret = Time.FromMinutes((float)(distance / AvgTravelSpeed));
            return ret;
        }

        [RunParameter("BikeShare Constant", 0f, "A constant applied to a tour that requires a bike share.")]
        public float BikeShareConstant;

        public bool CalculateTourDependentUtility(ITripChain chain, int tripIndex, out float dependentUtility, out Action<ITripChain> onSelection)
        {
            onSelection = null;
            if (BikeShareZones.Count > 0)
            {
                var trips = chain.Trips;
                // if this trip is a possible bike share trip
                if (BikeShareZones.Contains(trips[tripIndex].OriginalZone.ZoneNumber) &&
                    BikeShareZones.Contains(trips[tripIndex].DestinationZone.ZoneNumber))
                {
                    // Replicate the tour logic to see if
                    var lastPlace = trips[0].OriginalZone;
                    var homeZone = lastPlace;
                    bool firstWasBike = trips[0].Mode == this;
                    for (int i = 0; i < trips.Count; i++)
                    {
                        var trip = trips[i];
                        if (trip.Mode == this)
                        {
                            var oZone = trip.OriginalZone;
                            if (oZone != lastPlace)
                            {
                                // See if this could be a bike-share trip
                                if (BikeShareZones.Contains(oZone.ZoneNumber) && BikeShareZones.Contains(trip.DestinationZone.ZoneNumber))
                                {
                                    // only apply the constant if the first ride share trip is this one
                                    dependentUtility = i == tripIndex ? BikeShareConstant : 0f;
                                    return true;
                                }
                            }
                            lastPlace = trip.DestinationZone;
                        }
                    }
                }
            }
            dependentUtility = 0f;
            return true;
        }
    }
}