import re, socket, Levenshtein as lev
from collections import defaultdict

NWORDS = defaultdict(lambda: 1)
for word in re.split('\s+', (open('big.txt').read())):
    NWORDS[word.lower()] += 1

alphabet = dict()
for idx, word in enumerate(NWORDS.keys()):
    NWORDS[idx] = word.lower()
    for char in word:
        alphabet[char] = True
alphabet = alphabet.keys()

def edits1(word):
    s = [(word[:i], word[i:]) for i in range(len(word) + 1)]
    deletes    = [a + b[1:] for a, b in s if b]
    transposes = [a + b[1] + b[0] + b[2:] for a, b in s if len(b)>1]
    replaces   = [a + c + b[1:] for a, b in s for c in alphabet if b]
    inserts    = [a + c + b     for a, b in s for c in alphabet]
    return set(deletes + transposes + replaces + inserts)

def known_edits2(word):
    return set(e2 for e1 in edits1(word) for e2 in edits1(e1) if e2 in NWORDS)


data = open('0643/SHEFFIELDDAT.643').read()

corrections = [re.sub('\s+', ' ', item).split(' ') for item in data.split('\n')][:-1]

num = 0
correct = 0
for correction in corrections:
    if lev.distance(correction[0], correction[1]) > 2:
        print 'Distance greater than 2', correction
        continue
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.connect(('127.0.0.1', 11000))
    s.sendall(correction[1] + '<EOF>')
    res = s.recv(8000)
    num += 1
    if res == 'Not Found':
        res = ''

    corrected1 = set(res.split())
    corrected2 = known_edits2(correction[1].lower())

    difference = corrected1 - corrected2
    if len(difference) > 0:
        print correction
        print corrected1
        print corrected2
        print difference
        for word in difference:
            if word not in NWORDS:
                print 'not found in NWORDS ' + word
        exit()
        
    if (res.lower() + 's').find(correction[0].lower()) != -1:
        correct += 1
    else:
        print 'Correction not found by either algorithm'
        print '    ', correction, corrected1

print correct, num
print float(correct) / num
