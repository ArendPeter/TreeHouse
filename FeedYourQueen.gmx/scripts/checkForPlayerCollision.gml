var enemyX = x;
var enemyY = y;
var enemySpriteHeight = sprite_height;
var enemySpriteWidth = sprite_width;
var currentState = state;
var enemyDamage = damage;

with(oPlayer)
{
    if(point_distance(x,y,enemyX,enemyY) < 55)
    {
        if(currentState = STATE_INCHARGE)
        {
            if(!invincible)
            {
                currentHP -= enemyDamage;
                invincibilityTimer = room_speed*2;
                invincible = true;
            }
            instance_create(x,y,oShakeControl);
        }
    }
}
