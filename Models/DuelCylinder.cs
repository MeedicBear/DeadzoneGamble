using System;

namespace MeedicBear.DeadzoneGamble.Models
{
    public class DuelCylinder
    {
        public int Capacity { get; }
        public int LiveChamberIndex { get; private set; }
        public int CurrentPull { get; private set; }
        public bool[] ChamberVisited { get; }

        private readonly Random _rand = new Random();

        public DuelCylinder(int capacity = 6)
        {
            Capacity = capacity > 0 ? capacity : 6;
            ChamberVisited = new bool[Capacity];
            ResetAndSpin();
        }

        public void ResetAndSpin()
        {
            LiveChamberIndex = _rand.Next(0, Capacity);
            CurrentPull = 0;
            for (int i = 0; i < Capacity; i++)
            {
                ChamberVisited[i] = false;
            }
        }

        public void Spin()
        {
            LiveChamberIndex = _rand.Next(0, Capacity);
            CurrentPull = 0;
            for (int i = 0; i < Capacity; i++)
            {
                ChamberVisited[i] = false;
            }
        }

        /// <summary>
        /// Pulls the trigger on the current chamber.
        /// Returns true if the live round fired (BANG), false if empty click.
        /// </summary>
        public bool PullTrigger()
        {
            int pullIndex = CurrentPull;
            ChamberVisited[pullIndex] = true;
            CurrentPull++;

            if (pullIndex == LiveChamberIndex)
            {
                return true; // BANG!
            }

            return false; // CLICK!
        }

        public int GetRemainingChambers()
        {
            int remaining = Capacity - CurrentPull;
            return remaining > 0 ? remaining : 1;
        }

        public float GetFatalProbability()
        {
            int remaining = GetRemainingChambers();
            return (1f / remaining) * 100f;
        }
    }
}
