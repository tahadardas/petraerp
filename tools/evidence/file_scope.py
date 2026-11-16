import sys,re
p=sys.argv[1]
raw=open(p,'rb').read().decode('utf-8'); bom=raw.startswith('﻿'); t=raw.lstrip('﻿')
crlf='\r\n' in t; t=t.replace('\r\n','\n')
m=re.search(r'^namespace ([\w.]+)\n\{\n',t,re.M)
if not m: print('no block namespace'); sys.exit(0)
head=t[:m.start()]; body=t[m.end():]
body=body.rstrip('\n')
assert body.endswith('}')
body=body[:-1].rstrip('\n')
lines=[ (l[4:] if l.startswith('    ') else l) for l in body.split('\n')]
t=head+'namespace '+m.group(1)+';\n'+'\n'.join(lines)+'\n'
if crlf: t=t.replace('\n','\r\n')
open(p,'wb').write((('﻿' if bom else '')+t).encode('utf-8')); print('converted',p)
