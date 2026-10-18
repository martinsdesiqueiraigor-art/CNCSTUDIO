import { mountToolEditor } from './tools-ui.js';
// Casillas Code: configurador de setup no celular. O SVG é uma projeção ilustrativa, não geometria de colisão.
export function openSetup(json) {
  return new Promise(resolve => {
    let data;
    try { data = JSON.parse(json); } catch { resolve(null); return; }
    // Formato 2 do setup: o bruto vem sempre preenchido (sugestão quando não informado).
    // Prisma: (X, Y) é o canto; redondo e tubo: (X, Y) é o centro; Z é o fundo.
    const stock = data.Stock;
    if (!stock) { resolve(null); return; }
    const round0 = Number(stock.Kind) !== 0;
    // Posição do conjunto no programa: centro do bruto em XY e topo em Z
    const origin = {
      X: round0 ? +stock.X : +stock.X + (+stock.Width) / 2,
      Y: round0 ? +stock.Y : +stock.Y + (+stock.Depth) / 2,
      Z: +stock.Z + (+stock.Height)
    };
    const old = data.Fixtures?.[0];
    const fixture = old ? {...old} : {Kind:0,Name:'',X:0,Y:0,Z:-30,Width:150,Depth:110,Height:30,JawOpening:60,JawThickness:12,Rotation:0};
    const root = document.createElement('div');
    root.style.cssText = 'position:fixed;inset:0;z-index:10030;background:#0d1117;color:#e6edf3;overflow-y:auto;overscroll-behavior:contain;font:15px system-ui,sans-serif;padding:14px 14px 50px;box-sizing:border-box';
    const heading = document.createElement('h2'); heading.textContent='Setup de Usinagem'; heading.style.margin='0 0 10px';
    const subtitle = document.createElement('p'); subtitle.textContent='Configure o bruto e a fixação. A imagem muda antes de salvar.';subtitle.style.cssText='color:#9baab9;font-size:13px;margin:0 0 14px';
    const preview = document.createElement('section');
    preview.style.cssText='position:relative;background:#121d29;border:1px solid #30445a;border-radius:10px;padding:8px;margin-bottom:12px';
    const svg = document.createElementNS('http://www.w3.org/2000/svg','svg');
    svg.setAttribute('viewBox','0 0 360 225');svg.style.cssText='width:100%;height:auto;max-height:180px;display:block';
    const note=document.createElement('div');note.style.cssText='color:#8dbbd0;font:12px monospace;line-height:1.6';
    const dimensionCard=document.createElement('section');
    dimensionCard.style.cssText='background:#101b26;border:1px solid #30445a;border-radius:10px;padding:12px;margin:0 0 12px';
    const dimensionTitle=document.createElement('strong');dimensionTitle.textContent='Referência dimensional · mm';
    const dimensionHelp=document.createElement('p');dimensionHelp.textContent='Toque em uma cota para editar a medida. Planta e elevação atualizam com os campos.';dimensionHelp.style.cssText='color:#9baab9;font-size:12px;line-height:1.5';
    const dimensionSvg=document.createElementNS('http://www.w3.org/2000/svg','svg');
    dimensionSvg.setAttribute('viewBox','0 0 360 320');dimensionSvg.style.cssText='width:100%;max-height:350px;display:block;touch-action:manipulation';
    dimensionCard.append(dimensionTitle,dimensionHelp,dimensionSvg);
    preview.append(svg,note);
    const form=document.createElement('form');form.style.cssText='display:flex;flex-direction:column;gap:12px';
    const section=(title)=>{const fieldset=document.createElement('fieldset');fieldset.style.cssText='border:1px solid #303d4b;border-radius:9px;padding:12px;margin:0;display:flex;flex-direction:column;gap:9px';const legend=document.createElement('legend');legend.textContent=title;legend.style.cssText='color:#f0883e;font-weight:700;padding:0 5px';fieldset.append(legend);form.append(fieldset);return fieldset;};
    const select=(parent,label,items,value,onChange)=>{const row=document.createElement('label');row.style.cssText='display:flex;flex-direction:column;gap:5px;color:#aab7c6';row.textContent=label;const el=document.createElement('select');el.style.cssText='width:100%;box-sizing:border-box;background:#161b22;color:white;border:1px solid #4a5665;border-radius:7px;min-height:44px;padding:8px;font-size:16px';items.forEach((item,i)=>{const opt=document.createElement('option');opt.value=String(i);opt.textContent=item;el.append(opt)});el.value=String(value);el.addEventListener('change',()=>{onChange(+el.value);renderFields();draw()});row.append(el);parent.append(row);return el;};
    const field=(parent,label,obj,key,positive=false)=>{const row=document.createElement('label');row.style.cssText='display:grid;grid-template-columns:minmax(0,1fr) minmax(115px,1fr);align-items:center;gap:12px;min-height:46px';const text=document.createElement('span');text.textContent=label;const input=document.createElement('input');input.type='number';input.dataset.setupKey=key;input.inputMode='decimal';input.step='any';input.required=true;input.value=String(obj[key]??0);input.style.cssText='min-width:0;width:100%;box-sizing:border-box;background:#161b22;color:white;border:1px solid #4a5665;border-radius:7px;min-height:44px;padding:8px;font-size:17px';input.addEventListener('input',()=>{obj[key]=input.value===''?NaN:Number(input.value);draw()});if(positive)input.min='0.001';row.append(text,input);parent.append(row);return input;};
    const stockBox=section('01 · Bruto');
    let stockKind=Number(stock.Kind??0);
    select(stockBox,'Tipo de bruto',['Prisma retangular','Redondo maciço','Tubo'],stockKind,v=>{stockKind=v;stock.Kind=v});
    const stockFields=document.createElement('div');stockFields.style.cssText='display:flex;flex-direction:column;gap:8px';stockBox.append(stockFields);
    const fixtureBox=section('02 · Fixação');
    let fixKind=old ? Number(old.Kind)+1 : 0;
    select(fixtureBox,'Dispositivo',['Sem fixação','Morsa','Grampo','Placa de três castanhas'],fixKind,v=>{fixKind=v});
    const fixFields=document.createElement('div');fixFields.style.cssText='display:flex;flex-direction:column;gap:8px';fixtureBox.append(fixFields);
    const originBox=section('03 · Posição no programa');
    const info=document.createElement('p');info.textContent='Coordenadas do programa (zero de peça = 0). O bruto e a fixação compartilham o mesmo centro XY.';info.style.cssText='color:#9baab9;font-size:12px;margin:0';originBox.append(info);
    field(originBox,'Centro do bruto X (mm)',origin,'X');field(originBox,'Centro do bruto Y (mm)',origin,'Y');field(originBox,'Topo do bruto Z (mm)',origin,'Z');
    const toolBox=section('04 · Ferramenta');
    const toolEditor = mountToolEditor(toolBox, data);
    fixtureBox.after(dimensionCard);
    const error=document.createElement('div');error.style.cssText='color:#ff9a83;font-size:13px';
    const actions=document.createElement('div');actions.style.cssText='display:flex;gap:10px';
    const button=(label,color)=>{const el=document.createElement('button');el.type='button';el.textContent=label;el.style.cssText='flex:1;min-height:48px;border:1px solid #4a5665;border-radius:8px;background:'+color+';color:white;font-weight:700;font-size:15px';actions.append(el);return el;};
    const cancel=button('Cancelar','#30363d'),save=button('Salvar setup','#b45b1f');
    form.append(error,actions);root.append(heading,subtitle,preview,form);document.body.append(root);
    const close=value=>{root.remove();resolve(value)};
    cancel.addEventListener('click',()=>close(null));
    function renderFields(){
      stockFields.replaceChildren();fixFields.replaceChildren();
      if(stockKind===0){field(stockFields,'Comprimento X (mm)',stock,'Width',true);field(stockFields,'Largura Y (mm)',stock,'Depth',true);field(stockFields,'Espessura Z (mm)',stock,'Height',true);}
      else {field(stockFields,'Diâmetro externo (mm)',stock,'Diameter',true);if(stockKind===2)field(stockFields,'Diâmetro interno (mm)',stock,'InnerDiameter',true);field(stockFields,stockKind===1?'Altura (mm)':'Comprimento (mm)',stock,'Height',true);}
      if(fixKind===1){for(const [l,k] of [['Comprimento da base (mm)','Width'],['Largura da base (mm)','Depth'],['Altura da base (mm)','Height'],['Abertura entre mordentes (mm)','JawOpening'],['Espessura dos mordentes (mm)','JawThickness'],['Rotação Z (graus)','Rotation']])field(fixFields,l,fixture,k,k!=='Rotation');}
      else if(fixKind===2){for(const [l,k] of [['Comprimento do apoio (mm)','Width'],['Largura do apoio (mm)','Depth'],['Altura do apoio (mm)','Height'],['Espessura do grampo (mm)','JawThickness'],['Rotação Z (graus)','Rotation']])field(fixFields,l,fixture,k,k!=='Rotation');}
      else if(fixKind===3){for(const [l,k] of [['Diâmetro do corpo (mm)','Width'],['Altura do corpo (mm)','Height'],['Abertura das castanhas (mm)','JawOpening'],['Largura da castanha (mm)','JawThickness'],['Rotação das castanhas (graus)','Rotation']])field(fixFields,l,fixture,k,k!=='Rotation');}
      draw();
    }
    const fmt=n=>Number.isFinite(+n)?(+n).toLocaleString('pt-BR',{maximumFractionDigits:2}):'—';
    function drawDimensions(){
      const isPrism=stockKind===0, isChuck=fixKind===3;
      const w=isPrism?+stock.Width:+stock.Diameter, depth=isPrism?+stock.Depth:+stock.Diameter;
      const h=+stock.Height, fw=+fixture.Width, fd=isChuck?fw:+fixture.Depth;
      const maxW=Math.max(w||1,fixKind?fw:0),maxD=Math.max(depth||1,fixKind?fd:0);
      const scale=Math.min(1.25,230/Math.max(maxW,maxD,1));
      const x=180,y=103,ww=Math.max(4,w*scale),dd=Math.max(4,depth*scale);
      const fww=Math.max(4,fw*scale),fdd=Math.max(4,fd*scale);
      const rect=(a,b,c,e,fill,stroke)=>'<rect x="'+a+'" y="'+b+'" width="'+c+'" height="'+e+'" fill="'+fill+'" stroke="'+stroke+'"/>';
      const circle=(a,b,r,fill,stroke)=>'<circle cx="'+a+'" cy="'+b+'" r="'+r+'" fill="'+fill+'" stroke="'+stroke+'"/>';
      let s='<rect width="360" height="320" fill="#101b26"/><text x="12" y="18" fill="#9baab9" font-size="11">PLANTA · X / Y</text>';
      if(fixKind){
        if(isChuck)s+=circle(x,y,fww/2,'#526879','#b3c2cf');
        else s+=rect(x-fww/2,y-fdd/2,fww,fdd,'#526879','#b3c2cf');
      }
      s+=isPrism?rect(x-ww/2,y-dd/2,ww,dd,'#277d89','#73c9e8'):circle(x,y,ww/2,'#277d89','#73c9e8');
      if(stockKind===2)s+=circle(x,y,Math.max(1,+stock.InnerDiameter*scale/2),'#101b26','#73c9e8');
      if(fixKind===1){
        const gap=Math.min(fdd*.85,Math.max(2,+fixture.JawOpening*scale));
        const t=Math.max(3,+fixture.JawThickness*scale);
        s+=rect(x-fww/2,y-gap/2-t,fww,t,'#a4b2bd','#e1e8ed')+rect(x-fww/2,y+gap/2,fww,t,'#a4b2bd','#e1e8ed');
      }
      if(isChuck){
        for(let i=0;i<3;i++){const a=(i*120+(+fixture.Rotation||0))*Math.PI/180;const r=Math.min(fww*.42,Math.max(ww*.5,+fixture.JawOpening*scale*.5));s+=rect(x+Math.cos(a)*r-6,y+Math.sin(a)*r-6,12,12,'#a4b2bd','#e1e8ed');}
      }
      const dims=[];
      const dim=(letter,key,value,x1,y1,x2,y2,tx,ty)=>{
        const id='dimension-'+letter;
        s+='<g data-dim="'+letter+'" style="cursor:pointer"><path d="M'+x1+' '+y1+'L'+x2+' '+y2+'" stroke="#9cb4c5" stroke-width="1"/><circle cx="'+x1+'" cy="'+y1+'" r="2" fill="#7dd3fc"/><circle cx="'+x2+'" cy="'+y2+'" r="2" fill="#7dd3fc"/><rect x="'+(tx-28)+'" y="'+(ty-12)+'" width="68" height="19" rx="4" fill="#223446"/><text x="'+tx+'" y="'+ty+'" font-size="11" fill="#e8f3fa">'+letter+' · '+fmt(value)+'</text></g>';
        dims.push([letter,key]);
      };
      dim('A',isPrism?'Width':'Diameter',w,x-ww/2,184,x+ww/2,184,142,201);
      if(isPrism)dim('B','Depth',depth,42,y-dd/2,42,y+dd/2,10,107);
      if(stockKind===2)dim('C','InnerDiameter',stock.InnerDiameter,x-+stock.InnerDiameter*scale/2,y,x+stock.InnerDiameter*scale/2,y,142,116);
      s+='<path d="M12 218H348" stroke="#30445a"/><text x="12" y="237" fill="#9baab9" font-size="11">ELEVAÇÃO · X / Z</text>';
      const frontW=Math.min(240,ww),frontH=Math.min(55,Math.max(5,h*scale*.55));
      const z=295;
      if(fixKind)s+=rect(x-fww/2,z, fww,Math.min(18,Math.max(5,+fixture.Height*scale*.3)),'#526879','#b3c2cf');
      s+=rect(x-frontW/2,z-frontH,frontW,frontH,'#277d89','#73c9e8');
      dim('D','Height',h,315,z-frontH,315,z,280,z-frontH/2);
      if(fixKind)dim('E','fixture.Width',fw,x-fww/2,254,x+fww/2,254,143,250);
      dimensionSvg.innerHTML=s;
      dimensionSvg.querySelectorAll('[data-dim]').forEach(g=>g.addEventListener('click',()=>{
        const pair=dims.find(v=>v[0]===g.getAttribute('data-dim'));if(!pair)return;
        const key=pair[1], container=key.startsWith('fixture.')?fixFields:stockFields;
        const property=key.replace('fixture.','');
        const inputs=[...container.querySelectorAll('input')];
        const target=inputs.find(el=>el.dataset.setupKey===property);
        if(target){target.focus();target.select();target.scrollIntoView({behavior:'smooth',block:'center'});}
      }));
    }
    function draw(){
      const kind=stockKind, fix=fixKind;
      const sw=kind===0?Math.max(1,+stock.Width||1):Math.max(1,+stock.Diameter||1);
      const sd=kind===0?Math.max(1,+stock.Depth||1):sw;
      const sh=Math.max(1,+stock.Height||1);
      const fw=fix===0?0:Math.max(1,+fixture.Width||1);
      const fd=fix===0?0:fix===3?fw:Math.max(1,+fixture.Depth||1);
      const scale=Math.min(1.15,210/Math.max(sw,sd,fw,fd,1));
      const sx=sw*scale,sy=sd*scale,fx=fw*scale,fy=fd*scale;
      const cx=180,cy=122,base=157;
      const ell=(x,y,rx,ry,fill,stroke)=>'<ellipse cx="'+x+'" cy="'+y+'" rx="'+rx+'" ry="'+ry+'" fill="'+fill+'" stroke="'+stroke+'" stroke-width="1.5"/>';
      const rect=(x,y,w,h,fill,stroke)=>'<rect x="'+x+'" y="'+y+'" width="'+w+'" height="'+h+'" fill="'+fill+'" stroke="'+stroke+'" stroke-width="1.5"/>';
      let parts='<rect width="360" height="225" fill="#121d29"/><path d="M15 196H345 M180 20V205" stroke="#243849" stroke-dasharray="3 6"/>';
      if(fix===1){parts+=rect(cx-fx/2,base-6,fx,Math.max(9,fixture.Height*scale*.4),'#526879','#a9b7c6');const jaw=Math.max(3,Math.min(22,+fixture.JawThickness*scale));const gap=Math.max(3,Math.min(Math.max(3,fy-jaw*2),Math.max(5,+fixture.JawOpening*scale)));const jy=base-14;parts+=rect(cx-gap/2-jaw,jy-14,jaw,29,'#91a2b0','#c2ccd6')+rect(cx+gap/2,jy-14,jaw,29,'#91a2b0','#c2ccd6');}
      if(fix===2){parts+=rect(cx-fx/2,base,fx,Math.max(7,fixture.Height*scale*.4),'#526879','#a9b7c6');parts+=rect(cx-fx*.25,base-18,fx*.5,Math.max(5,fixture.JawThickness*scale*.35),'#91a2b0','#c2ccd6');}
      if(fix===3){const r=fx/2;parts+=ell(cx,base+Math.max(6,fixture.Height*scale*.2),r,r*.48,'#3a4d5c','#a9b7c6');parts+=ell(cx,base,r,r*.48,'#526879','#a9b7c6');parts+=ell(cx,base,r*.65,r*.3,'#213a4a','#a9b7c6');}
      // O bruto apoia na face da fixação; as três castanhas são desenhadas por cima dele.
      const topY=base-18-Math.min(45,sh*scale*.35);
      const stockBottom=topY+Math.max(8,sh*scale*.35);
      if(kind===0){const x=cx-sx/2,y=topY-sy*.2;parts+=rect(x,y,sx,Math.max(6,sy*.4),'#27776e','#78d4c7');parts+=rect(x,y+Math.max(6,sy*.4),sx,Math.max(6,sh*scale*.35),'#17525a','#78d4c7');}
      else {const r=sx/2,ry=r*.48;parts+=ell(cx,topY+Math.max(8,sh*scale*.35),r,ry,'#17525a','#78d4c7');parts+=rect(cx-r,topY,r*2,Math.max(8,sh*scale*.35),'#216a68','#78d4c7');parts+=ell(cx,topY,r,ry,'#2b8278','#78d4c7');if(kind===2){const inner=Math.min(.95,Math.max(0,(+stock.InnerDiameter||0)/sw));parts+=ell(cx,topY,r*inner,ry*inner,'#121d29','#78d4c7');}}
      if(fix===3){const r=fx/2;const contact=(kind===0?Math.max(sw,sy):sx)/2;const jawWidth=Math.max(4,Math.min(24,(+fixture.JawThickness||10)*scale));const jawRadius=Math.max(0,Math.min(Math.max(0,r-jawWidth/2),Math.max(contact+jawWidth/2,(+fixture.JawOpening||0)*scale/2)));for(let i=0;i<3;i++){const a=(i*120+(+fixture.Rotation||0))*Math.PI/180;const x=cx+Math.cos(a)*jawRadius,y=base-12+Math.sin(a)*jawRadius*.48;const jawHeight=Math.max(9,Math.min(18,sh*scale*.3));parts+=rect(x-jawWidth/2,y-jawHeight/2,jawWidth,jawHeight,'#a4b2bd','#d4dce3');parts+='<path d="M'+(x-jawWidth/2)+' '+(y-jawHeight/2+3)+'h'+jawWidth+'" stroke="#e6edf3" stroke-width="1"/>';}}
      parts+='<path d="M22 195h35m-35 0v-31" stroke="#58a6ff" stroke-width="2"/><text x="62" y="200" fill="#58a6ff" font-size="11">X</text><text x="14" y="158" fill="#58a6ff" font-size="11">Y</text><text x="12" y="17" fill="#c9d1d9" font-size="12">Montagem ilustrativa</text>';
      svg.innerHTML=parts;
      const stockText=kind===0?fmt(stock.Width)+' × '+fmt(stock.Depth)+' × '+fmt(stock.Height):'Ø'+fmt(stock.Diameter)+(kind===2?' / Ø'+fmt(stock.InnerDiameter):'')+' × '+fmt(stock.Height);
      drawDimensions();
      note.textContent=(['Prisma','Redondo','Tubo'][kind])+' · '+stockText+' mm · '+(['Sem fixação','Morsa','Grampo','Placa 3 castanhas'][fix])+' · centro XY compartilhado';
    }
    save.addEventListener('click',()=>{
      error.textContent='';
      if(!form.reportValidity())return;
      const positive=(...vals)=>vals.every(v=>Number.isFinite(v)&&v>0);
      if(!positive(stock.Height,...(stockKind===0?[stock.Width,stock.Depth]:[stock.Diameter]))){error.textContent='Informe dimensões positivas do bruto.';return;}
      if(stockKind===2&&!(Number.isFinite(stock.InnerDiameter)&&stock.InnerDiameter>0&&stock.InnerDiameter<stock.Diameter)){error.textContent='O diâmetro interno deve ser menor que o externo.';return;}
      if(fixKind!==0&&!positive(fixture.Width,fixture.Height,fixture.JawThickness,...(fixKind===3?[]:[fixture.Depth]),...(fixKind===2?[]:[fixture.JawOpening]))){error.textContent='Confira as dimensões da fixação.';return;}
      const toolError = toolEditor.validate(); if(toolError){error.textContent=toolError;return;}
      stock.Kind=stockKind;
      if(![origin.X,origin.Y,origin.Z].every(Number.isFinite)){error.textContent='Confira a posição do bruto.';return;}
      // Alinha bruto e fixação em XY pelo centro informado; o topo do bruto fica em Z informado.
      stock.X=stockKind===0?origin.X-stock.Width/2:origin.X;
      stock.Y=stockKind===0?origin.Y-stock.Depth/2:origin.Y;
      stock.Z=origin.Z-stock.Height;
      if(fixKind!==0){fixture.Kind=fixKind-1;fixture.Depth=fixKind===3?fixture.Width:fixture.Depth;fixture.X=origin.X-fixture.Width/2;fixture.Y=origin.Y-fixture.Depth/2;fixture.Z=stock.Z-fixture.Height;data.Fixtures=[fixture];}else data.Fixtures=[];
      toolEditor.save();
      close(JSON.stringify(data));
    });
    renderFields();
  });
}
