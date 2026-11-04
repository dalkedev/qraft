"""Generate the editable academic specification and its original diagrams."""
from pathlib import Path
import json
from PIL import Image, ImageDraw, ImageFont
from docx import Document
from docx.shared import Mm, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

BASE = Path(__file__).resolve().parent
FIG = BASE / 'figures'
FIG.mkdir(exist_ok=True)
FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
BOLD = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
BLUE = '#234E70'

def diagram(name, title, boxes, arrows, footnote='', compact=False):
    im=Image.new('RGB',(1800,600 if compact else 1050),'white'); d=ImageDraw.Draw(im)
    f=lambda n,b=False:ImageFont.truetype(BOLD if b else FONT,n)
    d.text((55,35),title,font=f(36,True),fill=BLUE)
    for x,y,w,h,label,kind in boxes:
        color='#FFF4D5' if kind=='plan' else ('#E8F3EE' if kind=='db' else '#EDF3F8')
        d.rounded_rectangle((x,y,x+w,y+h),radius=14,fill=color,outline=BLUE,width=3)
        rows=label.split('\n'); font=f(27,True)
        for i,row in enumerate(rows):
            bb=d.textbbox((0,0),row,font=font)
            d.text((x+(w-bb[2])/2,y+(h-len(rows)*38)/2+i*38),row,font=font,fill='#142C40')
    for x1,y1,x2,y2,label in arrows:
        if y1==125 and y2==125:
            d.line((x1,160,x1,125,x2,125,x2,160),fill=BLUE,width=4)
        d.line((x1,y1,x2,y2),fill=BLUE,width=4)
        import math
        angle=math.atan2(y2-y1,x2-x1)
        d.polygon([(x2,y2),(x2-20*math.cos(angle-.45),y2-20*math.sin(angle-.45)),(x2-20*math.cos(angle+.45),y2-20*math.sin(angle+.45))],fill=BLUE)
        if label:
            bb=d.textbbox((0,0),label,font=f(23))
            mx=(x1+x2)/2;my=(y1+y2)/2-33
            d.rectangle((mx-bb[2]/2-6,my-3,mx+bb[2]/2+6,my+30),fill='white')
            d.text((mx-bb[2]/2,my),label,font=f(23),fill=BLUE)
    if footnote:
        for i,line in enumerate(footnote.split('\n')):d.text((55,(505 if compact else 930)+i*33),line,font=f(23),fill='#333333')
    im.save(FIG/name,dpi=(300,300))

diagram('architecture.png','Komponenty i przepływ aktualnego systemu',[
(70,160,400,130,'Vue 3 + TypeScript\nQraft Lab','ui'),(690,160,400,130,'ASP.NET Core\nSymulator API','ui'),(1290,160,420,130,'ASP.NET Core\nQraft API','ui'),
(690,390,400,130,'simulator.db\nźródło + outbox + wynik','db'),(70,620,400,140,'RabbitMQ\nżądania / odpowiedzi','ui'),(690,620,400,140,'Qraft Worker\nwalidacja + domena','ui'),(1290,620,420,140,'qraft.db\nwejście + wynik + outbox','db')],[(470,225,690,225,'HTTP'),(470,125,1290,125,'HTTP odczyt wyników'),(890,290,890,390,''),(690,420,230,620,'żądanie'),(470,690,690,690,''),(1090,690,1290,690,'zapis'),(1500,620,1500,290,'odczyt'),(690,740,470,740,'wynik'),(390,620,690,500,'odpowiedź')], 'Każda usługa zarządza własną bazą. RabbitMQ nie zastępuje trwałego outboxa.')
diagram('data_model.png','Model trwałości — dwie niezależne bazy SQLite',[
(70,150,750,160,'ReceivedInspections\nPK MessageId; hash, wejście, wynik','db'),(70,570,350,220,'SampleAssessments\nPK Id; FK MessageId\nUNIQUE\nMessageId + Code','db'),(470,570,350,220,'ResultOutbox\nPK Id\nUNIQUE\nSourceMessageId','db'),
(1000,130,700,130,'ScenarioFixtures\nPK Code; szablon i oczekiwany wynik','ui'),(1000,340,700,130,'SimulationRuns\nPK Id; FK ScenarioCode','ui'),(1000,550,700,130,'SourceMessages\nPK MessageId; FK RunId; UNIQUE ResultId','ui'),(1000,760,700,130,'SubmissionOutbox\nPK Id; FK SourceMessageId','ui')],[(245,310,245,570,'1 → 0..3'),(645,310,645,570,'1 → 1'),(1350,260,1350,340,'1 → 0..N'),(1350,470,1350,550,'1 → 0..N'),(1350,680,1350,760,'1 → 0..N')], 'Qraft (lewa strona) i symulator (prawa) posiadają własne modele EF Core i migracje.\nPowiązanie ReceivedInspections → ResultOutbox 1:1 jest regułą zapisu aplikacji.')
diagram('interface.png','Panel Vue — schemat nawigacji i informacji',[
(65,135,490,125,'1. Wybór scenariusza\ngood / błędny / graniczny','ui'),(660,135,480,125,'2. Uruchomienie\nprzebiegu symulacji','ui'),(1240,135,490,125,'3. Stan wysyłki\ni odpowiedź','ui'),
(1240,330,490,125,'4. Szczegóły wyniku\nstatus / próbki / błędy','ui'),(660,330,480,125,'5. Replay źródła\nstały MessageId','ui')],[(555,195,660,195,''),(1140,195,1240,195,''),(1485,260,1485,330,''),(1240,392,1140,392,''),(900,330,900,260,'')], 'Schemat na podstawie kodu; nie jest zrzutem wykonanej sesji.\nPanel nie realizuje ręcznego zwalniania partii.', compact=True)
diagram('planned_process.png','Planowane E1/E2 — jeszcze niezaimplementowane',[
(65,135,490,125,'Badanie E1\npo regranulacji','plan'),(660,135,480,125,'Akceptacja E1\ni przygotowanie','plan'),(1240,135,490,125,'Badanie E2\npo przygotowaniu','plan'),
(1240,330,490,125,'Zwolnienie partii\npo zgodnym E2','plan'),(660,330,480,125,'Poprawa i nowe badanie\nalbo odrzucenie','plan')],[(555,195,660,195,''),(1140,195,1240,195,''),(1485,260,1485,330,''),(1240,260,1100,330,''),(310,260,660,392,'')], 'Model docelowy; obecny kod nie wymusza tych przejść.\nOcena jakości nie jest decyzją o zwolnieniu materiału.', compact=True)

doc=Document();sec=doc.sections[0]
sec.page_width=Mm(210);sec.page_height=Mm(297)
sec.top_margin=Mm(20);sec.bottom_margin=Mm(19);sec.left_margin=Mm(23);sec.right_margin=Mm(23)
sec.header_distance=Mm(9);sec.footer_distance=Mm(9)
for name in ['Normal','Title','Subtitle','Heading 1','Heading 2','Caption']:
 s=doc.styles[name];s.font.name='DejaVu Serif';s.font.color.rgb=RGBColor(0,0,0)
normal=doc.styles['Normal'];normal.font.size=Pt(10.5);normal.paragraph_format.line_spacing=1.08;normal.paragraph_format.space_after=Pt(7)
for name,size in [('Heading 1',15),('Heading 2',12)]:
 s=doc.styles[name];s.font.size=Pt(size);s.paragraph_format.keep_with_next=True
sec.different_first_page_header_footer=True
h=sec.header.paragraphs[0];h.text='Qraft | Specyfikacja i projekt systemu';h.alignment=WD_ALIGN_PARAGRAPH.RIGHT;h.style='Caption'
p=sec.footer.paragraphs[0];p.alignment=WD_ALIGN_PARAGRAPH.CENTER
fld=OxmlElement('w:fldSimple');fld.set(qn('w:instr'),'PAGE');p._p.append(fld)
doc.core_properties.author='Mateusz Dalke; Oskar Kirko';doc.core_properties.last_modified_by='Mateusz Dalke; Oskar Kirko'
doc.core_properties.title='Qraft — specyfikacja i projekt systemu kontroli jakości';doc.core_properties.subject='Aktualna architektura ASP.NET Core, Vue, RabbitMQ i SQLite'

def para(t,center=False):
 p=doc.add_paragraph(t);p.alignment=WD_ALIGN_PARAGRAPH.CENTER if center else WD_ALIGN_PARAGRAPH.JUSTIFY;return p

def fig(name,caption):
 p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.keep_with_next=True
 p.add_run().add_picture(str(FIG/name),width=Mm(162))
 p=doc.add_paragraph(caption+'. Źródło: opracowanie własne.','Caption');p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_after=Pt(8)

def table(data):
 p=doc.add_paragraph(data['caption'],'Caption');p.paragraph_format.keep_with_next=True
 t=doc.add_table(rows=1,cols=2);t.style='Table Grid';t.autofit=False;t.columns[0].width=Mm(52);t.columns[1].width=Mm(110)
 for i,s in enumerate(data['headers']): t.rows[0].cells[i].text=s
 for vals in data['rows']:
  for c,s in zip(t.add_row().cells,vals):c.text=s
 for ri,row in enumerate(t.rows):
  pr=row._tr.get_or_add_trPr();pr.append(OxmlElement('w:cantSplit'))
  if ri==0:pr.append(OxmlElement('w:tblHeader'))
  for c in row.cells:
   for p in c.paragraphs:
    p.paragraph_format.space_after=Pt(4);p.paragraph_format.space_before=Pt(4);p.paragraph_format.line_spacing=1
    for r in p.runs:r.font.size=Pt(9);r.bold=ri==0

for s in ['POLITECHNIKA BYDGOSKA','Informatyka Stosowana','III rok studiów | V semestr | grupa 1','Programowanie w Środowisku Windows','Rok akademicki 2026/2027']:para(s,True)
p=para('Qraft',True);p.paragraph_format.space_before=Pt(60);p.runs[0].font.size=Pt(30);p.runs[0].bold=True
p=para('Specyfikacja i projekt systemu\nkontroli jakości regranulatu PP',True);p.runs[0].font.size=Pt(18);p.runs[0].bold=True
para('C#/.NET · ASP.NET Core · Vue · RabbitMQ · SQLite',True)
p=para('Mateusz Dalke, nr indeksu 124382\nOskar Kirko, nr indeksu 123758',True);p.paragraph_format.space_before=Pt(65);p.runs[0].bold=True
para('Bydgoszcz 2026',True)
for page in json.loads((BASE/'content.json').read_text()):
 doc.add_page_break();doc.add_heading(page['title'],0 if False else 1)
 for t in page['paragraphs']:para(t)
 if 'table' in page:table(page['table'])
 if 'figure' in page:fig(page['figure'],page['caption'])
 for t in page.get('extra',[]):para(t)
 if 'figure2' in page:fig(page['figure2'],page['caption2'])
doc.save(BASE/'Qraft_specyfikacja.docx')
print('Generated DOCX, four diagrams and source; PDF rendering still required.')
