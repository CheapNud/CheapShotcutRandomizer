using System;
using System.Collections.Generic;
using System.Linq;
using CheapShotcutRandomizer.Core.Models;

namespace CheapShotcutRandomizer.Services;

/// <summary>
///
/// </summary>
/// <param name="TargetDuration"></param>
/// <param name="DurationWeight">This gives moderate preference for shorter videos. 0.1 to 1.0: Balanced selection with slight preference for shorter videos. 1.0 to 2.5: Moderate preference for shorter videos. 3.0 to 5.0: Strong preference for shorter videos. > 5: Extreme bias toward the shortest videos only. </param>
/// <param name="NumberOfVideosWeight">This adds a slight bias toward maximizing the number of videos included in the final selection. A value between 0.0 and 1.0 is usually reasonable. A higher weight(e.g., 0.5 or 0.8) may work well in scenarios where you have many short videos and want to prioritize their inclusion.</param>
public class SimulatedAnnealingVideoSelector(int TargetDuration = 3600, double DurationWeight = 0.5, double NumberOfVideosWeight = 0.5)
{
        private static readonly Random rand = new();
        //Set the starting temperature high to allow more freedom in early iterations.
        private const double InitialTemperature = 10000.0;
        //Controls how quickly the algorithm cools down. A slower cooling rate allows more exploration.
        private const double CoolingRate = 0.002;
        // Weight applied to duration preference. Increase for shorter video preference.

        public List<Entry> SelectVideos(List<Entry> videoList)
        {
            // Initial random selection
            List<Entry> currentSolution = GetRandomSelection(videoList);
            double currentScore = GetWeightedScore(currentSolution);

            List<Entry> bestSolution = new(currentSolution);
            double bestScore = currentScore;

            double temperature = InitialTemperature;

            while (temperature > 1)
            {
                // Generate a neighboring solution by making a small change
                List<Entry> newSolution = GetNeighboringSolution(currentSolution, videoList);
                double newScore = GetWeightedScore(newSolution);

                // Decide whether to accept the new solution
                if (AcceptanceProbability(currentScore, newScore, temperature) > rand.NextDouble())
                {
                    currentSolution = newSolution;
                    currentScore = newScore;
                }

                // Update the best solution found so far
                int actualDuration = newSolution.Sum(v => v.Duration);
                if (actualDuration <= TargetDuration && currentScore > bestScore)
                {
                    bestSolution = new List<Entry>(currentSolution);
                    bestScore = currentScore;
                }

                // Cool down
                temperature *= 1 - CoolingRate;
            }

            return bestSolution;
        }

        private static double AcceptanceProbability(double currentScore, double newScore, double temperature)
        {
            // If the new solution is better, accept it
            if (newScore > currentScore)
            {
                return 1.0;
            }
            // Otherwise, accept it with a probability that decreases as the temperature decreases
            return Math.Exp((newScore - currentScore) / temperature);
        }

        // Weighted score using exponential scaling with capping and considering the number of videos
        private double GetWeightedScore(List<Entry> videos)
        {
            // Define a cap to prevent overflow based on the number of videos
            long maxValue = long.MaxValue / videos.Count;

            // Calculate the weighted duration with capping
            double weightedDuration = videos.Sum(v => Math.Min((long)(v.Duration * Math.Pow(v.Duration, DurationWeight)), maxValue));

            // Factor in the number of videos
            double videoCountScore = videos.Count * NumberOfVideosWeight;

            // Return the combined score: bias toward both shorter durations and more videos
            return weightedDuration + videoCountScore;
        }

        private List<Entry> GetRandomSelection(List<Entry> videoList)
        {
            // Shuffle the list and take a random selection
            List<Entry> shuffledVideos = [.. videoList.OrderBy(x => rand.Next())];
            List<Entry> selectedVideos = [];
            int totalDuration = 0;

            foreach (var video in shuffledVideos)
            {
                if (totalDuration + video.Duration <= TargetDuration)
                {
                    selectedVideos.Add(video);
                    totalDuration += video.Duration;
                }
                else
                {
                    break;
                }
            }

            return selectedVideos;
        }

        private static List<Entry> GetNeighboringSolution(List<Entry> currentSolution, List<Entry> videoList)
        {
            // Make a small change to the current solution (swap one video)
            List<Entry> newSolution = new(currentSolution);

            // Choose a video to replace
            int indexToRemove = rand.Next(newSolution.Count);
            newSolution.RemoveAt(indexToRemove);

            // Choose a new video to add that isn't already in the solution
            Entry newVideo;
            do
            {
                newVideo = videoList[rand.Next(videoList.Count)];
            } while (newSolution.Contains(newVideo));

            newSolution.Add(newVideo);

            return newSolution;
        }
    }
