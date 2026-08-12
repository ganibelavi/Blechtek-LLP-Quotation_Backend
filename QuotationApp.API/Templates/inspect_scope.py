import zipfile
import xml.etree.ElementTree as ET

with zipfile.ZipFile('QuotationTemplate.docx') as z:
    data = z.read('word/document.xml')
root = ET.fromstring(data)
ns = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
for tr in root.findall('.//w:tr', ns):
    cells = tr.findall('w:tc', ns)
    texts = [''.join(t.itertext()).strip() for tc in cells for t in tc.findall('.//w:t', ns)]
    if any(text == 'Key' for text in texts) or any('MODROW:' in text for text in texts):
        print('ROW', len(cells), texts)
