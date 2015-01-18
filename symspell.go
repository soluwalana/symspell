// Port of the C# version of the code
// License:
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License, 
// version 3.0 (LGPL-3.0) as published by the Free Software Foundation.
// http://www.opensource.org/licenses/LGPL-3.0
// 
// Port Author: SamO
// Original Author: Wolf Garbe <wolf.garbe@faroo.com>
//
package main


import (
	"fmt"
	"sync"
	"errors"
	"math"
	"hash"
	"hash/fnv"
)

const EDIT_DISTANCE uint = 2


/* very loosely defined hash table to reduce size of the 
   symspell dictionary. No Deletion supported */
type DefinitionOrdinals struct {
	sync.RWMutex
	definitions map[uint64][]string
	hasher hash.Hash64
}

func NewDefinitionOrdinals() (*DefinitionOrdinals){
	var do DefinitionOrdinals
	do.definitions = make(map[uint64][]string)
	do.hasher = fnv.New64()
	return &do
}

/* Takes the definition string and computes a hash for it, 
   the hash is then used as the index in a map and the definition
   is stored there. */
func (self *DefinitionOrdinals) Add(definition string) uint64 {
	self.Lock()
	defer self.Unlock()
	self.hasher.Reset()
	self.hasher.Write([]byte(definition))
	sum := self.hasher.Sum64()
	self.definitions[sum] = append(self.definitions[sum], definition)
	return sum
 }

/*  Retrieves the definitions given the hash 
    (may be more than one due to hash collisions) */
func (self *DefinitionOrdinals) Get(hash uint64) []string {
	self.RLock()
	defer self.RUnlock()
	if data, ok := self.definitions[hash]; ok {
		return data
	}
	return []string{}
}
	

type DictItem struct {
	Term string
	Count uint
	Suggestions map[string]uint
	Definitions map[uint64]bool
}

type SuggestItem struct {
	Term string
	Distance uint
	Count uint
	Definitions []string
}


type SymSpell struct {
	Dictionary map[string]*DictItem
	Ordinals *DefinitionOrdinals
	sync.RWMutex
}

func NewSymSpell() (*SymSpell) {
	var symSpell SymSpell
	symSpell.Dictionary = make(map[string]*DictItem)
	symSpell.Ordinals = NewDefinitionOrdinals()
	return &symSpell
}

//for every word there all deletes with an edit distance of 1..editDistanceMax created and added to the dictionary
//every delete entry has a suggestions list, which points to the original term(s) it was created from
//The dictionary may be dynamically updated (word frequency and new words) at any time by calling createDictionaryEntry
func (self *SymSpell) AddEntry(term string, definitions []string) (bool, error) {
	if term == "" {
		return false, errors.New("Empty string can't be the term")
	}
	
	added := false
	self.Lock()
	defer self.Unlock()
	
	var entry *DictItem
	
	if found, ok := self.Dictionary[term]; ok {
		//already exists:
        //1. word appears several times
        //2. word1==deletes(word2) 
		entry = found

	} else {
		entry = new(DictItem)
		entry.Suggestions = make(map[string]uint)
		entry.Definitions = make(map[uint64]bool)
		self.Dictionary[term] = entry
	}

	entry.Count ++

	for _, def := range definitions {
		hash := self.Ordinals.Add(def)
		entry.Definitions[hash] = true
	}

	//edits/suggestions are created only once, no matter how often word occurs
    //edits/suggestions are created only as soon as the word occurs in the corpus, 
    //even if the same term existed before in the dictionary as an edit from another word
	if entry.Term == "" {
		added = true
		entry.Term = term

		// Create delete suggestions
		for edit, distance := range self.Edits(term, 0, nil, true) {
			
			if entry2, ok := self.Dictionary[edit]; ok {
				//already exists:
                //1. word1==deletes(word2) 
                //2. deletes(word1)==deletes(word2) 
				if _, ok := entry2.Suggestions[term]; !ok {
					entry2.Suggestions[term] = distance;
				}
			} else {
				suggested := new(DictItem)
				suggested.Suggestions = make(map[string]uint)
				suggested.Definitions = make(map[uint64]bool)
				suggested.Suggestions[term] = distance
				self.Dictionary[edit] = suggested
			}
		}
	}
	return added, nil
}

func (self *SymSpell) Edits(term string, distance uint, edits map[string]uint, recursive bool) map[string]uint {
	distance ++
	if edits == nil {
		edits = make(map[string]uint)
	}
	if (len(term) > 1) {
		for i := 0; i < len(term); i ++ {
			delete := term[:i] + term[i + 1:]
			if _, ok := edits[delete]; !ok {
				edits[delete] = distance
				if (recursive && (distance < EDIT_DISTANCE)) {
					self.Edits(delete, distance, edits, recursive)
				}
			}
		}
	}
	return edits
}

func (self *SymSpell) Lookup(input string) []SuggestItem {
	self.RLock()
	defer self.RUnlock()
	
	candidates := make(map[string]uint)
	candidates[input] = 0

	suggestions := make(map[string]SuggestItem)
	
	for len(candidates) > 0 {
		candidate := arbitraryKey(candidates) // Supposed to be entry 0
		cDistance := candidates[candidate]
		delete(candidates, candidate)

		if cDistance > EDIT_DISTANCE {
			break
		}

		if value, ok := self.Dictionary[candidate]; ok {
			if value.Term != "" {
				// Correct Term
				var si SuggestItem
				si.Term = value.Term
				si.Count = value.Count
				si.Distance = cDistance
				if _, ok := suggestions[si.Term]; !ok {
					suggestions[si.Term] = si
				}
			}

			for suggest, sDistance := range value.Suggestions {

				//save some time 
                //skipping double items early
				if _, ok := suggestions[suggest]; !ok {
					realDistance := TrueDistance(suggest, sDistance, candidate, cDistance, input);

					if realDistance <= EDIT_DISTANCE {
						if value2, ok := self.Dictionary[suggest]; ok {
							var si SuggestItem
							si.Term = value2.Term
							si.Count = value2.Count
							si.Distance = realDistance

							suggestions[si.Term] = si
						}
					}
				}
			}
		}
		
		if cDistance < EDIT_DISTANCE {
			for delete, dDistance := range self.Edits(candidate, cDistance, nil, false) {
				if _, ok := candidates[delete]; !ok {
					candidates[delete] = dDistance
				}
			}
		}			
	}

	var items []SuggestItem
	for _, suggest := range suggestions {
		items = append(items, suggest)
	}
	return items
}

func TrueDistance(suggest string, sDistance uint, candidate string, cDistance uint, input string) uint {
	//We allow simultaneous edits (deletes) of editDistanceMax on on both the dictionary and the input term. 
    //For replaces and adjacent transposes the resulting edit distance stays <= editDistanceMax.
    //For inserts and deletes the resulting edit distance might exceed editDistanceMax.
    //To prevent suggestions of a higher edit distance, we need to calculate the resulting edit distance, if there are simultaneous edits on both sides.
    //Example: (bank==bnak and bank==bink, but bank!=kanb and bank!=xban and bank!=baxn for editDistanceMaxe=1)
    //Two deletes on each side of a pair makes them all equal, but the first two pairs have edit distance=1, the others edit distance=2.

	if suggest == input { 
		return 0
	} else if sDistance == 0 {
		return cDistance
	} else if cDistance == 0 {
		return sDistance
	} else {
		return LevDistance(suggest, input)
	}
}

// Damerau–Levenshtein distance algorithm and code 
// from http://en.wikipedia.org/wiki/Damerau%E2%80%93Levenshtein_distance

func LevDistance(source string, target string) uint {

	// Set up arrays for levenshtein on unicode characters
	s1 := make([]rune, len(source)) 
	s2 := make([]rune, len(target))
	m, n := 0, 0
	sd := make(map[rune]int)
	
	for _, char := range source {
		s1[m] = char
		if _, ok := sd[char]; !ok {
			sd[char] = 0
		}
		m ++
	}
	for _, char := range target {
		s2[n] = char
		if _, ok := sd[char]; !ok {
			sd[char] = 0
		}
		n ++
	}

	H := make([][]int, m + 2)
	for i := range H {
		H[i] = make([]int, n + 2)
	}
	INF := m + n
	H[0][0] = INF
	for i := 0; i <= m; i ++ {
		H[i + 1][1] = i
		H[i + 1][0] = INF
	}
	for j := 0; j <= n; j ++ {
		H[1][j + 1] = j
		H[0][j + 1] = INF
	}

	for i := 1; i <= m; i ++ {
		DB := 0
		for j := 1; j <= n; j ++ {
			i1 := sd[s2[j - 1]]
			j1 := DB

			if s1[i - 1] == s2[j - 1] {
				H[i + 1][j + 1] = H[i][j]
				DB = j
			} else {
				H[i + 1][j + 1] = int(math.Min(float64(H[i][j]), math.Min(float64(H[i + 1][j]), float64(H[i][j + 1])))) + 1
			}

			H[i + 1][j + 1] = int(math.Min(float64(H[i +1][j + 1]), float64(H[i1][j1] + (i - i1 - 1) + 1 + (j - j1 - 1))))
		}

		sd[s1[i - 1]] = i
	}
	return uint(H[m + 1][n + 1])
}


func arbitraryKey(input map[string]uint) string {
	for key := range input {
		return key
	}
	return ""
}

func main() {	
	ss:= NewSymSpell()
	ss.AddEntry("hello", []string{"What is the definition for hello?",})
	fmt.Println(ss.Lookup("zello"))
    fmt.Println('\x00')
}