// SymSpell: 1000x faster through Symmetric Delete spelling correction algorithm
//
// The Symmetric Delete spelling correction algorithm reduces the complexity of edit candidate generation and dictionary lookup 
// for a given Damerau-Levenshtein distance. It is three orders of magnitude faster and language independent.
// Opposite to other algorithms only deletes are required, no transposes + replaces + inserts.
// Transposes + replaces + inserts of the input term are transformed into deletes of the dictionary term.
// Replaces and inserts are expensive and language dependent: e.g. Chinese has 70,000 Unicode Han characters!
//
// Copyright (C) 2012 Wolf Garbe, FAROO Limited
// Version: 1.6
// Author: Wolf Garbe <wolf.garbe@faroo.com>
// Maintainer: Wolf Garbe <wolf.garbe@faroo.com>
// URL: http://blog.faroo.com/2012/06/07/improved-edit-distance-based-spelling-correction/
// Description: http://blog.faroo.com/2012/06/07/improved-edit-distance-based-spelling-correction/
//
// License:
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License, 
// version 3.0 (LGPL-3.0) as published by the Free Software Foundation.
// http://www.opensource.org/licenses/LGPL-3.0
//
// Usage: single word + Enter:  Display spelling suggestions
//        Enter without input:  Terminate the program

// 
// Server code stolen from msdn
// http://msdn.microsoft.com/en-us/library/fx6588te%28v=vs.110%29.aspx
//

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// State object for reading client data asynchronously
public class StateObject {
    // Client  socket.
    public Socket workSocket = null;
    // Size of receive buffer.
    public const int BufferSize = 1024;
    // Receive buffer.
    public byte[] buffer = new byte[BufferSize];
    // Received data string.
    public StringBuilder sb = new StringBuilder();  
}

static public class SymSpell
{
    private static int editDistanceMax = 2;
    private static int verbose = 2;
    //0: top suggestion
    //1: all suggestions of smallest edit distance 
    //2: all suggestions <= editDistanceMax (slower, no early termination)

    // Thread signal.
    public static ManualResetEvent allDone = new ManualResetEvent(false);

    public static void StartListening() {

        // Establish the local endpoint for the socket.
        // The DNS name of the computer
        // running the listener is "host.contoso.com".
        IPAddress localAddr = Dns.GetHostEntry("localhost").AddressList[0];
        IPEndPoint localEndPoint = new IPEndPoint(localAddr, 11000);

        // Create a TCP/IP socket.
        Socket listener = new Socket(AddressFamily.InterNetwork,
            SocketType.Stream, ProtocolType.Tcp );

        // Bind the socket to the local endpoint and listen for incoming connections.
        try {
            listener.Bind(localEndPoint);
            listener.Listen(100);

            while (true) {
                // Set the event to nonsignaled state.
                allDone.Reset();

                // Start an asynchronous socket to listen for connections.
                Console.WriteLine("Waiting for a connection...");
                listener.BeginAccept( 
                    new AsyncCallback(AcceptCallback),
                    listener );

                // Wait until a connection is made before continuing.
                allDone.WaitOne();
            }

        } catch (Exception e) {
            Console.WriteLine(e.ToString());
        }

        Console.WriteLine("\nPress ENTER to continue...");
        Console.Read();
        
    }

    public static void AcceptCallback(IAsyncResult ar) {
        // Signal the main thread to continue.
        Console.WriteLine("In Accept Callback");
        allDone.Set();

        // Get the socket that handles the client request.
        Socket listener = (Socket) ar.AsyncState;
        Socket handler = listener.EndAccept(ar);

        // Create the state object.
        StateObject state = new StateObject();
        state.workSocket = handler;
        handler.BeginReceive( state.buffer, 0, StateObject.BufferSize, 0,
            new AsyncCallback(ReadCallback), state);
    }

    public static void ReadCallback(IAsyncResult ar) {
        Console.WriteLine("In Read Callback");
        String content = String.Empty;
        
        // Retrieve the state object and the handler socket
        // from the asynchronous state object.
        StateObject state = (StateObject) ar.AsyncState;
        Socket handler = state.workSocket;

        // Read data from the client socket. 
        int bytesRead = handler.EndReceive(ar);

        if (bytesRead > 0) {
            // There  might be more data, so store the data received so far.
            state.sb.Append(Encoding.ASCII.GetString(
                state.buffer,0,bytesRead));

            // Check for end-of-file tag. If it is not there, read 
            // more data.
            content = state.sb.ToString();
            int eof = content.IndexOf("<EOF>");
            if (eof > -1) {
                // All the data has been read from the 
                // client. Display it on the console.
                Console.WriteLine("Read {0} bytes from socket. \n Data : {1}", content.Length, content );
                content = content.Trim().ToLower();
                
                if (!string.IsNullOrEmpty(content)) {
                    string res = Correct(content.Substring(0, eof), "en");
                    if (!string.IsNullOrEmpty(res)) {
                        Send(handler, res);
                    } else {
                        Send(handler, "Not Found");
                    }

                } else {
                    // Echo the data back to the client.
                    Send(handler, content);
                }
            } else {
                // Not all data received. Get more.
                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                new AsyncCallback(ReadCallback), state);
            }
        }
    }
    
     private static void Send(Socket handler, String data) {
        // Convert the string data to byte data using ASCII encoding.
        Console.WriteLine("Sending data");
        Console.WriteLine(data);
        byte[] byteData = Encoding.ASCII.GetBytes(data);

        // Begin sending the data to the remote device.
        handler.BeginSend(byteData, 0, byteData.Length, 0,
            new AsyncCallback(SendCallback), handler);
    }

    private static void SendCallback(IAsyncResult ar) {
        Console.WriteLine("In Send Callback");
        try {
            // Retrieve the socket from the state object.
            Socket handler = (Socket) ar.AsyncState;

            // Complete sending the data to the remote device.
            int bytesSent = handler.EndSend(ar);
            Console.WriteLine("Sent {0} bytes to client.", bytesSent);

            handler.Shutdown(SocketShutdown.Both);
            handler.Close();

        } catch (Exception e) {
            Console.WriteLine(e.ToString());
        }
    }


    private class dictionaryItem
    {
        public string term = "";
        public List<editItem> suggestions = new List<editItem>();
        public int count = 0;

        public override bool Equals(object obj)
        {
            return Equals(term, ((dictionaryItem)obj).term);
        }
     
        public override int GetHashCode()
        {
            return term.GetHashCode(); 
        }       
    }

    private class editItem
    {
        public string term = "";
        public int distance = 0;

        public override bool Equals(object obj)
        {
            return Equals(term, ((editItem)obj).term);
        }
     
        public override int GetHashCode()
        {
            return term.GetHashCode();
        }       
    }

    private class suggestItem
    {
        public string term = "";
        public int distance = 0;
        public int count = 0;

        public override bool Equals(object obj)
        {
            return Equals(term, ((suggestItem)obj).term);
        }
     
        public override int GetHashCode()
        {
            return term.GetHashCode();
        }       
    }

    private static Dictionary<string, dictionaryItem> dictionary = new Dictionary<string, dictionaryItem>();

    //create a non-unique wordlist from sample text
    //language independent (e.g. works with Chinese characters)
    private static IEnumerable<string> parseWords(string text)
    {
        
        MatchCollection matches =  Regex.Matches(text.ToLower(), @"[\w\-\d_]+");
        return matches.Cast<Match>().Select(m => m.Value);
    }

    //for every word there all deletes with an edit distance of 1..editDistanceMax created and added to the dictionary
    //every delete entry has a suggestions list, which points to the original term(s) it was created from
    //The dictionary may be dynamically updated (word frequency and new words) at any time by calling createDictionaryEntry
    private static bool CreateDictionaryEntry(string key, string language)
    {
        bool result = false;
        dictionaryItem value;
        if (dictionary.TryGetValue(language+key, out value))
        {
            //already exists:
            //1. word appears several times
            //2. word1==deletes(word2) 
            value.count++;
        }
        else
        {
            value = new dictionaryItem();
            value.count++;
            dictionary.Add(language+key, value);
        }

        //edits/suggestions are created only once, no matter how often word occurs
        //edits/suggestions are created only as soon as the word occurs in the corpus, 
        //even if the same term existed before in the dictionary as an edit from another word
        if (string.IsNullOrEmpty(value.term))
        {
            result = true;
            value.term = key;

            //create deletes
            foreach (editItem delete in Edits(key, 0, true))
            {
                editItem suggestion = new editItem();
                suggestion.term = key;
                suggestion.distance = delete.distance;

                dictionaryItem value2;
                if (dictionary.TryGetValue(language+delete.term, out value2))
                {
                    //already exists:
                    //1. word1==deletes(word2) 
                    //2. deletes(word1)==deletes(word2) 
                    if (!value2.suggestions.Contains(suggestion)) AddLowestDistance(value2.suggestions, suggestion);
                }
                else
                {
                    value2 = new dictionaryItem();
                    value2.suggestions.Add(suggestion);
                    dictionary.Add(language+delete.term, value2);
                }
            }
        }
        return result;
    }

    //create a frequency disctionary from a corpus
    private static void CreateDictionary(string corpus, string language)
    {
        if (!File.Exists(corpus))
        {
            Console.Error.WriteLine("File not found: " + corpus);
            return;
        }

        Console.Write("Creating dictionary ...");
        long wordCount = 0;
        foreach (string key in parseWords(File.ReadAllText(corpus)))
        {
            if (CreateDictionaryEntry(key, language)) wordCount++;
        }
        Console.WriteLine("\rDictionary created: " + wordCount.ToString() + " words, " + dictionary.Count.ToString() + " entries, for edit distance=" + editDistanceMax.ToString());
    }

    //save some time and space
    private static void AddLowestDistance(List<editItem> suggestions, editItem suggestion)
    {
        //remove all existing suggestions of higher distance, if verbose<2
        if ((verbose < 2) && (suggestions.Count > 0) && (suggestions[0].distance > suggestion.distance)) suggestions.Clear();
        //do not add suggestion of higher distance than existing, if verbose<2
        if ((verbose == 2) || (suggestions.Count == 0) || (suggestions[0].distance >= suggestion.distance)) suggestions.Add(suggestion);
    }

    //inexpensive and language independent: only deletes, no transposes + replaces + inserts
    //replaces and inserts are expensive and language dependent (Chinese has 70,000 Unicode Han characters)
    private static List<editItem> Edits(string word, int editDistance, bool recursion)
    {
        editDistance++;
        List<editItem> deletes = new List<editItem>();
        if (word.Length > 1)
        {
            for (int i = 0; i < word.Length; i++)
            {
                editItem delete = new editItem();
                delete.term=word.Remove(i, 1);
                delete.distance=editDistance;
                if (!deletes.Contains(delete))
                {
                    deletes.Add(delete);
                    //recursion, if maximum edit distance not yet reached
                    if (recursion && (editDistance < editDistanceMax)) 
                    {
                        foreach (editItem edit1 in Edits(delete.term, editDistance,recursion))
                        {
                            if (!deletes.Contains(edit1)) deletes.Add(edit1); 
                        }
                    }                   
                }
            }
        }

        return deletes;
    }

    private static int TrueDistance(editItem dictionaryOriginal, editItem inputDelete, string inputOriginal)
    {
        //We allow simultaneous edits (deletes) of editDistanceMax on on both the dictionary and the input term. 
        //For replaces and adjacent transposes the resulting edit distance stays <= editDistanceMax.
        //For inserts and deletes the resulting edit distance might exceed editDistanceMax.
        //To prevent suggestions of a higher edit distance, we need to calculate the resulting edit distance, if there are simultaneous edits on both sides.
        //Example: (bank==bnak and bank==bink, but bank!=kanb and bank!=xban and bank!=baxn for editDistanceMaxe=1)
        //Two deletes on each side of a pair makes them all equal, but the first two pairs have edit distance=1, the others edit distance=2.

        if (dictionaryOriginal.term == inputOriginal) return 0; else
        if (dictionaryOriginal.distance == 0) return inputDelete.distance;
        else if (inputDelete.distance == 0) return dictionaryOriginal.distance;
        else return DamerauLevenshteinDistance(dictionaryOriginal.term, inputOriginal);//adjust distance, if both distances>0
    }

    private static List<suggestItem> Lookup(string input, string language, int editDistanceMax)
    {
        List<editItem> candidates = new List<editItem>();

        //add original term
        editItem item = new editItem();
        item.term = input;
        item.distance = 0;
        candidates.Add(item);
 
        List<suggestItem> suggestions = new List<suggestItem>();
        dictionaryItem value;

        while (candidates.Count>0)
        {
            editItem candidate = candidates[0];
            candidates.RemoveAt(0);

            //save some time
            //early termination
            //suggestion distance=candidate.distance... candidate.distance+editDistanceMax                
            //if canddate distance is already higher than suggestion distance, than there are no better suggestions to be expected
            if ((verbose < 2)&&(suggestions.Count > 0)&&(candidate.distance > suggestions[0].distance)) goto sort;
            if (candidate.distance > editDistanceMax) goto sort;  

            if (dictionary.TryGetValue(language+candidate.term, out value))
            {
                if (!string.IsNullOrEmpty(value.term))
                {
                    //correct term
                    suggestItem si = new suggestItem();
                    si.term = value.term;
                    si.count = value.count;
                    si.distance = candidate.distance;

                    if (!suggestions.Contains(si))
                    {
                        suggestions.Add(si);
                        //early termination
                        if ((verbose < 2) && (candidate.distance == 0)) goto sort;     
                    }
                }

                //edit term (with suggestions to correct term)
                dictionaryItem value2;
                foreach (editItem suggestion in value.suggestions)
                {
                    //save some time 
                    //skipping double items early
                    if (suggestions.Find(x => x.term == suggestion.term) == null)
                    {
                        int distance = TrueDistance(suggestion, candidate, input);
                     
                        //save some time.
                        //remove all existing suggestions of higher distance, if verbose<2
                        if ((verbose < 2) && (suggestions.Count > 0) && (suggestions[0].distance > distance)) suggestions.Clear();
                        //do not process higher distances than those already found, if verbose<2
                        if ((verbose < 2) && (suggestions.Count > 0) && (distance > suggestions[0].distance)) continue;

                        if (distance <= editDistanceMax)
                        {
                            if (dictionary.TryGetValue(language+suggestion.term, out value2))
                            {
                                suggestItem si = new suggestItem();
                                si.term = value2.term;
                                si.count = value2.count;
                                si.distance = distance;

                                suggestions.Add(si);
                            }
                        }
                    }
                }
            }//end foreach

            //add edits 
            if (candidate.distance < editDistanceMax)
            {
                foreach (editItem delete in Edits(candidate.term, candidate.distance,false))
                {
                    if (!candidates.Contains(delete)) candidates.Add(delete);
                }
            }
        }//end while

        sort: suggestions = suggestions.OrderBy(c => c.distance).ThenByDescending(c => c.count).ToList();
        if ((verbose == 0)&&(suggestions.Count>1))  return suggestions.GetRange(0, 1); else return suggestions;
    }

    private static string Correct(string input, string language)
    {
        List<suggestItem> suggestions = null;
    
        /*
        //Benchmark: 1000 x Lookup
        Stopwatch stopWatch = new Stopwatch();
        stopWatch.Start();
        for (int i = 0; i < 1000; i++)
        {
            suggestions = Lookup(input,language,editDistanceMax);
        }
        stopWatch.Stop();
        Console.WriteLine(stopWatch.ElapsedMilliseconds.ToString());
        */
        
        //check in dictionary for existence and frequency; sort by edit distance, then by word frequency
        suggestions = Lookup(input, language, editDistanceMax);
        string best = String.Empty;
        //display term and frequency
        foreach (var suggestion in suggestions)
        {
            best += " " + suggestion.term;
            Console.WriteLine( suggestion.term + " " + suggestion.distance.ToString() + " " + suggestion.count.ToString());
        }
        if (verbose == 2) Console.WriteLine(suggestions.Count.ToString() + " suggestions");
        return best ;
    }

    private static void ReadFromStdIn()
    {
        string word;
        while (!string.IsNullOrEmpty(word = (Console.ReadLine() ?? "").Trim().ToLower()))
        {
            Correct(word,"en");
        }
    }

    public static void Main(string[] args)
    {
        //e.g. http://norvig.com/big.txt , or any other large text corpus
        CreateDictionary("big.txt","en");
        //ReadFromStdIn();
        StartListening();
    }

    // Damerau–Levenshtein distance algorithm and code 
    // from http://en.wikipedia.org/wiki/Damerau%E2%80%93Levenshtein_distance
    public static Int32 DamerauLevenshteinDistance(String source, String target)
    {
        Int32 m = source.Length;
        Int32 n = target.Length;
        Int32[,] H = new Int32[m + 2, n + 2];

        Int32 INF = m + n;
        H[0, 0] = INF;
        for (Int32 i = 0; i <= m; i++) { H[i + 1, 1] = i; H[i + 1, 0] = INF; }
        for (Int32 j = 0; j <= n; j++) { H[1, j + 1] = j; H[0, j + 1] = INF; }

        SortedDictionary<Char, Int32> sd = new SortedDictionary<Char, Int32>();
        foreach (Char Letter in (source + target))
        {
            if (!sd.ContainsKey(Letter))
                sd.Add(Letter, 0);
        }

        for (Int32 i = 1; i <= m; i++)
        {
            Int32 DB = 0;
            for (Int32 j = 1; j <= n; j++)
            {
                Int32 i1 = sd[target[j - 1]];
                Int32 j1 = DB;

                if (source[i - 1] == target[j - 1])
                {
                    H[i + 1, j + 1] = H[i, j];
                    DB = j;
                }
                else
                {
                    H[i + 1, j + 1] = Math.Min(H[i, j], Math.Min(H[i + 1, j], H[i, j + 1])) + 1;
                }

                H[i + 1, j + 1] = Math.Min(H[i + 1, j + 1], H[i1, j1] + (i - i1 - 1) + 1 + (j - j1 - 1));
            }

            sd[ source[ i - 1 ]] = i;
        }
        return H[m + 1, n + 1];
    }
}
