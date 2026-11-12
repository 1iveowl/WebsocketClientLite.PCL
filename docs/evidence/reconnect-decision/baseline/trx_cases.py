import sys, glob, os, xml.etree.ElementTree as ET
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def secs(d):
    h,m,s=d.split(':'); return int(h)*3600+int(m)*60+float(s)
rows=[]
for f in sorted(glob.glob(os.path.join(sys.argv[1],'*.trx'))):
    r=ET.parse(f).getroot()
    res=r.findall('.//t:UnitTestResult',ns)
    tot=sum(secs(x.get('duration')) for x in res)
    print(f"{os.path.basename(f)}: {len(res)} results, outcomes={sorted(set(x.get('outcome') for x in res))}, sum_case_seconds={tot:.3f}, max_case_seconds={max(secs(x.get('duration')) for x in res):.3f}")
    for x in sorted(res,key=lambda x:x.get('testName')):
        print(f"  {secs(x.get('duration')):7.3f}  {x.get('outcome'):7}  {x.get('testName')}")
