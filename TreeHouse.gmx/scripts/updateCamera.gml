var viewCenterX, viewCenterY, 
    centerAxis, centerSprite, 
    enemyW, enemyH;
//dimensions
var targetW = 960;
var targetH = 540;

//position


//tweening algorithm for smooth camera movement
view_wview += (targetW - view_wview) * tweenFactor;
view_hview += (targetH - view_hview) * tweenFactor;

view_xview += (targetX - view_xview) * tweenFactor;
view_yview += (targetY - view_yview) * tweenFactor;