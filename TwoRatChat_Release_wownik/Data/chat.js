var ggbg = /\[sml\]file.*?www\\(.*?).png\[\/sml\]/g;
var smlr = /\[sml\](.*?)\[\/sml\]/g;
var urlr = /\[url\](.*?)\[\/url\]/g;
var br = /\[b\](.*?)\[\/b\]/g;
var remainMessages = 30;
var chats = {
    delMe: [],
    oldMes: {}
};


function extractDomain(url) {
    var domain;
    //find & remove protocol (http, ftp, etc.) and get domain
    if (url.indexOf("://") > -1) {
        domain = url.split('/')[2];
    }
    else {
        domain = url.split('/')[0];
    }

    //find & remove port number
    domain = domain.split(':')[0];

    return domain;
};

function isOffscreen(el) {
    if( _boolAllowHide )
        return el.offset().top < _topHideY;
    return (el.offset().top + el.height()) < _topHideY;
};

function insertSmiles(text) {
    var oldText;

    var j = 10;

   // do {
    //    oldText = text;
        if (_showSmiles)
            text = text
				.replace(ggbg, "<img src='$1.png' class='message_smile' />") // BUGFIX :((
				.replace(smlr, "<img src='$1' class='message_smile' />")
        ;

        text = text
			.replace(br, "<b>$1</b>")
			.replace(urlr, function (lnk) {
			    return "<a href='" + lnk + "'>" + extractDomain(lnk) + "</a>";
			});

        if (_showImages)
            text = text
				.replace(urlr, "<img src='$1' class='message_img' />");

      //  if( (--j) < 0 ){
      //      text += "LOCK DETECTED!";
      //      break;
     //   }
 //   } while (oldText != text)

    return text;
}

function createMessage(mes) {
    var s = '';
    if (mes.source !== undefined) {
        s = "<img src='"+_rootAsset+"img/" + mes.source + ".png' class='message_source' />";
    };
	
	if( mes.icon !== undefined ){
        s += "<img src='" + mes.icon + "' class='message_icon' />";
	}

   /* if (mes.level !== undefined) {
        if (_showMode >= 1) {
            s += "<img class='message_level' src='" + mes.level.icon + "' />";
        }

        if (_showMode >= 2) {
            s += "<span class='message_exp'>[" + mes.exp + "/" + mes.level + "]</span>";
        }
    }*/
	
	//if( mes.exp !== undefined )
	//	s += "<span class='message_exp'>[" + mes.exp + "/" + mes.level + "]</span>";

    var mclass = "message";

    if (mes.source === undefined) {
        mclass = "messagesys";
        s += "<span class='message_textsys'>" + insertSmiles(mes.text);
    } else {
        if (mes.tome == 'True'){
            mclass = "messagetm";
            s += "<span class='message_name'>" + mes.name +
		         ": </span><span class='message_texttm'>" + insertSmiles(mes.text);
        }else
            s += "<span class='message_name'>" + mes.name +
				 ": </span><span class='message_text'>" + insertSmiles(mes.text);
    }

    s = "<div class='" + mclass + " "+_animShow+" animated'>" + s + "</span></div>"

    // Создается объект на основе разметки сообщения
    return $(s);
}

function onData(data) {
    try{
        var start = data.messages.length - remainMessages;
        if (start < 0) start = 0;

        for (var i = start; i < data.messages.length; ++i) {
            var mes = data.messages[i];
            if (chats.oldMes[mes.gid] == undefined) {
                try {
    				//console.log(mes);
                    var m = onNewMessage(mes);
                    chats.delMe.push(m);
                    chats.oldMes[mes.gid] = {
                        delay: 5,
                        message: m
                    };
                } catch (err) {
                   console.error(err);
                   //alert("failed: " + err);
                }
            }
        }

        

        //


        //$("#root").scrollTop($("#root")[0].scrollHeight);
    }catch(pika){
        alert(pika);
    }
    setTimeout(getData, 1000);
}

function validateAndScroll(){
    if (chats.delMe.length > 1) {
        var nDelMe = [];

        for (var j = 0; j < chats.delMe.length; ++j)
            if (chats.delMe[j].hasClass(_animHide)) {
                chats.delMe[j].remove();
            } else {
                nDelMe.push(chats.delMe[j])
            }

        for (var j = 0; j < chats.delMe.length; ++j) {
            var mes = chats.delMe[j];
            if (isOffscreen(mes))
                mes.addClass(_animHide)
        }
        chats.delMe = nDelMe;
    }

    $("#root").animate({ scrollTop: $("#root")[0].scrollHeight + 1000 }, 90);
    setTimeout(validateAndScroll, 100);
}

function onNewMessage(mes) {
    var sex = createMessage( mes );
    sex.appendTo("#root");
    return sex;
}

function getData() {
    $.getJSON(_rootChat)
        .done(onData)
        .fail(function (jqxhr, textStatus, error) {
            //console.log("failed: " + error);
            setTimeout(getData, 5000);
            //alert("failed: " + error);
        });
}

function onUpdate(){

}

function onStart(){
    getData();
    validateAndScroll();
}