/*
    Copyright 2017-2018 Travel Modelling Group, Department of Civil Engineering, University of Toronto

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
using Tasha.Common;
using System.IO;
using TMG.Input;
using Datastructure;
using TMG;

namespace Tasha.Validation.ModeChoice
{
    [ModuleInformation(Description = "This module is specifically designed to produce a TripRecords.csv that contains travel time information and better access station data.")]
    public sealed class ExtractTripRecordsWithLOS : IPostHouseholdIteration, IDisposable
    {
        [RootModule]
        public ITashaRuntime Root;

        [RunParameter("DAT Name", "DAT", "The name of the drive-access-transit mode.")]
        public string DATName;

        [SubModelInformation(Required = true, Description = "The location to save to.")]
        public FileLocation SaveTo;

        [RunParameter("Auto Network", "Auto", "The name of the auto network.")]
        public string AutoNetworkName;

        [RunParameter("Transit Network", "Transit", "The name of the transit network.")]
        public string TransitNetworkName;

        private INetworkData AutoNetwork;
        private ITripComponentData TransitNetwork;

        public string Name { get; set; }

        public float Progress { get; set; }

        public Tuple<byte, byte, byte> ProgressColour { get { return new Tuple<byte, byte, byte>(50, 150, 50); } }

        StreamWriter Writer;
        private SparseTwinIndex<float> ZoneDistances;
        private SparseArray<IZone> ZoneSystem;
        private ITashaMode DAT;
        private bool WriteThisIteration;

        public void HouseholdComplete(ITashaHousehold household, bool success)
        {
            if (WriteThisIteration)
            {
                var householdNumber = household.HouseholdId;
                lock (this)
                {
                    int personNumber = 0;
                    foreach (var person in household.Persons)
                    {
                        personNumber++;
                        var tripNumber = 1;
                        foreach (var tripChain in person.TripChains)
                        {
                            var numberOfWorkTrips = tripChain.Trips.Count(trip => trip.Purpose == Activity.PrimaryWork || trip.Purpose == Activity.SecondaryWork || trip.Purpose == Activity.WorkBasedBusiness);
                            var numberOfTripInTour = tripChain.Trips.Count;
                            var trips = tripChain.Trips;
                            for (int i = 0; i < trips.Count; i++)
                            {
                                SaveTrip(tripChain, trips[i], householdNumber, personNumber,
                                    tripNumber, numberOfWorkTrips, numberOfTripInTour, i);
                                tripNumber++;
                            }
                        }
                    }
                }
            }
        }

        public void HouseholdIterationComplete(ITashaHousehold household, int hhldIteration, int totalHouseholdIterations)
        {
            if (WriteThisIteration)
            {
                // The difference here and the normal version of this module is that we need to keep track
                // of the access stations.  In order to do this we are going to keep a list attached to each trip chain with the selected
                // station index, this should have minimal impact on performance since there is no need to hold a lock while doing so.
                foreach (var person in household.Persons)
                {
                    foreach (var tripChain in person.TripChains)
                    {
                        var trips = (tripChain.Trips);
                        for (int i = 0; i < trips.Count; i++)
                        {
                            if (trips[i].Mode == DAT)
                            {
                                // if there is a DAT trip in the tour extract out the access station and add it to our list
                                if (tripChain["AccessStation"] is IZone accessStationZone)
                                {
                                    var sparseZone = accessStationZone.ZoneNumber;
                                    var list = tripChain["AllAccessStations"] as List<(int, int, int)>;
                                    if (list == null)
                                    {
                                        tripChain.Attach("AllAccessStations", (list = new List<(int, int, int)>()));
                                    }
                                    // the last element is the access trip index
                                    list.Add((sparseZone, ZoneSystem.GetFlatIndex(sparseZone), i));
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void WriteHeader()
        {
            var allModes = Root.AllModes;
            Writer.WriteLine("HouseholdID,PersonID,TripNumber,OriginZone,DestinationZone,Purpose,TripStartTime,ActivityStartTime,Distance," +
                "NumberOfWorkTrips,NumberOfTripsInTour,AccessStation,ObservedMode,Weight,TotalTime");
        }

        private void SaveTrip(ITripChain chain, ITrip trip, int householdNumber, int personNumber,
            int tripNumber, int numberOfWorkTrips, int numberOfTripsInTour, int tripIndex)
        {
            var writer = Writer;
            var modesChosen = trip.ModesChosen;
            var datList = chain["AllAccessStations"] as List<(int sparse, int flat, int first)>;
            for (int i = 0; i < AllModes.Length; i++)
            {
                if (modesChosen.Any(m => m == AllModes[i]))
                {
                    if (AllModes[i] != DAT)
                    {
                        writer.Write(householdNumber);
                        writer.Write(',');
                        writer.Write(personNumber);
                        writer.Write(',');
                        writer.Write(tripNumber);
                        writer.Write(',');
                        writer.Write(trip.OriginalZone.ZoneNumber);
                        writer.Write(',');
                        writer.Write(trip.DestinationZone.ZoneNumber);
                        writer.Write(',');
                        writer.Write(GetPurposeName(trip.Purpose));
                        writer.Write(',');
                        writer.Write(trip.TripStartTime);
                        writer.Write(',');
                        writer.Write(trip.ActivityStartTime);
                        writer.Write(',');
                        writer.Write(GetTripDistance(trip));
                        writer.Write(',');
                        writer.Write(numberOfWorkTrips);
                        writer.Write(',');
                        writer.Write(numberOfTripsInTour);
                        writer.Write(',');
                        writer.Write(0); // access station
                        writer.Write(',');
                        writer.Write(AllModes[i].ModeName);
                        writer.Write(',');
                        writer.Write((float)modesChosen.Count(m => m == AllModes[i]) / modesChosen.Length);
                        writer.Write(',');
                        writer.Write(AllModes[i].TravelTime(trip.OriginalZone, trip.DestinationZone, trip.ActivityStartTime).ToMinutes());
                        writer.WriteLine();
                    }
                    else
                    {
                        // This can happen if there is no possible access station but DAT is the chosen mode
                        // A review of GTAModel for this rare case is in order.
                        if (datList != null)
                        {
                            foreach (var res in from el in datList
                                                group el by (el.sparse, el.first) into grouped
                                                select grouped)
                            {
                                var (sparse, flat, first) = res.First();
                                var total = res.Count();
                                writer.Write(householdNumber);
                                writer.Write(',');
                                writer.Write(personNumber);
                                writer.Write(',');
                                writer.Write(tripNumber);
                                writer.Write(',');
                                writer.Write(trip.OriginalZone.ZoneNumber);
                                writer.Write(',');
                                writer.Write(trip.DestinationZone.ZoneNumber);
                                writer.Write(',');
                                writer.Write(GetPurposeName(trip.Purpose));
                                writer.Write(',');
                                writer.Write(trip.TripStartTime);
                                writer.Write(',');
                                writer.Write(trip.ActivityStartTime);
                                writer.Write(',');
                                writer.Write(GetTripDistance(trip));
                                writer.Write(',');
                                writer.Write(numberOfWorkTrips);
                                writer.Write(',');
                                writer.Write(numberOfTripsInTour);
                                writer.Write(',');
                                writer.Write(sparse); // access station
                                writer.Write(',');
                                writer.Write(AllModes[i].ModeName);
                                writer.Write(',');
                                writer.Write((float)total / modesChosen.Length);
                                writer.Write(',');
                                // calculate the travel time for DAT manually since it is going to be lazy and just return a zero since no particular station has been chosen.
                                writer.Write(CalculateDATTime(trip.ActivityStartTime, trip.OriginalZone, flat, trip.DestinationZone, tripIndex == first));
                                writer.WriteLine();
                            }
                        }
                    }
                }
            }
        }

        private float CalculateDATTime(Time time, IZone originalZone, int accessFlat, IZone destinationZone, bool access)
        {
            var origin = ZoneSystem.GetFlatIndex(originalZone.ZoneNumber);
            var destination = ZoneSystem.GetFlatIndex(destinationZone.ZoneNumber);
            if (access)
            {
                return (AutoNetwork.TravelTime(origin, accessFlat, time)
                    + TransitNetwork.TravelTime(accessFlat, destination, time)).ToMinutes();
            }
            else
            {
                return (TransitNetwork.TravelTime(origin, accessFlat, time)
                    + AutoNetwork.TravelTime(accessFlat, destination, time)).ToMinutes();
            }
        }

        private float GetTripDistance(ITrip trip)
        {
            var origin = trip.OriginalZone.ZoneNumber;
            var dest = trip.DestinationZone.ZoneNumber;
            return ZoneDistances[origin, dest];
        }

        private string GetPurposeName(Activity purpose)
        {
            return Enum.GetName(typeof(Activity), purpose);
        }

        public void HouseholdStart(ITashaHousehold household, int householdIterations)
        {
            if (WriteThisIteration)
            {
                // Ensure that all of the data is removed before starting
                // This matters when chaining together runs in the same process
                foreach (var person in household.Persons)
                {
                    foreach (var tripChain in person.TripChains)
                    {
                        var stationList = tripChain["AllAccessStations"] as List<(int, int)>;
                        stationList?.Clear();
                    }
                }
            }
        }

        public void IterationFinished(int iteration, int totalIterations)
        {
            if (WriteThisIteration)
            {
                Writer.Close();
                Writer = null;
            }
        }

        private ITashaMode[] AllModes;

        public void IterationStarting(int iteration, int totalIterations)
        {
            WriteThisIteration = iteration == totalIterations - 1;
            if (WriteThisIteration)
            {
                ZoneSystem = Root.ZoneSystem.ZoneArray;
                ZoneDistances = Root.ZoneSystem.Distances;
                AllModes = Root.AllModes.ToArray();
                DAT = AllModes.First(m => m.Name == DATName);
                Writer = new StreamWriter(SaveTo);
                WriteHeader();
            }
        }

        public bool RuntimeValidation(ref string error)
        {
            foreach (var network in Root.NetworkData)
            {
                if (network.NetworkType == AutoNetworkName)
                {
                    AutoNetwork = network as INetworkData;
                }
                else if (network.NetworkType == TransitNetworkName)
                {
                    TransitNetwork = network as ITripComponentData;
                }
            }
            if (AutoNetwork == null)
            {
                error = "In '" + Name + "' we were unable to find an auto network called '" + AutoNetworkName + "'";
                return false;
            }
            if (TransitNetwork == null)
            {
                error = "In '" + Name + "' we were unable to find a transit network called '" + TransitNetworkName + "'";
                return false;
            }
            return true;
        }

        public void Dispose()
        {
            if (Writer != null)
            {
                Writer.Close();
                Writer = null;
            }
        }
    }
}
