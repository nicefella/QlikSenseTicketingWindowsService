  		$(document).ready(function() {
    		$("#submit").click(function(e) {  		
    			e.preventDefault();
				var data = {};
				data.username = $("#username").val();
				data.password = $("#password").val();

				$.ajax({
					type: 'POST',
					data:  data,
					contentType: 'application/json',
					url: '/',						
					success: function(datasent) {
						var obj = JSON.parse(datasent); 
						if (obj.Ticket != undefined) {
							var redirectURI = "https://" + obj.Host + "/" + obj.Prefix + "/hub/my/work" + '?QlikTicket=' + obj.Ticket;
							location.replace(redirectURI); 
							//$("p").html(datasent);
						} else {
							$("p").html("Username / password incorrect");
						}
					}
				});

    		});
  		});