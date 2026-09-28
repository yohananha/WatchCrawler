import re
import sys

text = open(sys.argv[1], encoding="utf-8").read()
strs = re.findall(r'"((?:[^"\\]|\\.)*)"', text)
chars = set()
for s in strs:
    chars.update(s)
print("".join(sorted(chars)))
